using HIM.Gateway.Models;
using HIM.Gateway.Services.SSH.Interfaces;
using Microsoft.DevTunnels.Ssh;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace HIM.Gateway.Services.SSH
{
    public class SshServerListener : ISshServerListener
    {
        private readonly ITuiEngine _tuiEngine;
        private readonly IHostKeyService _hostKeyService;
        private readonly IAuthenticationService _authenticationService;
        private readonly SshSettings _settings;
        private readonly SemaphoreSlim _connectionSemaphore;

        public SshServerListener(
            ITuiEngine tuiEngine,
            IHostKeyService hostKeyService,
            IAuthenticationService authenticationService,
            IOptions<SshSettings> settings)
        {
            _tuiEngine = tuiEngine;
            _hostKeyService = hostKeyService;
            _authenticationService = authenticationService;
            _settings = settings.Value;
            _connectionSemaphore = new SemaphoreSlim(_settings.MaxConnections, _settings.MaxConnections);
        }
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            var listener = new TcpListener(IPAddress.Any, _settings.Port);
            listener.Start();

            Console.WriteLine($"[Gateway] Listening for SSH connections on port: {_settings.Port} (Max: {_settings.MaxConnections})");

            try
            {
                while(!cancellationToken.IsCancellationRequested)
                {
                    // Wait for an available slot before accepting a new connection
                    await _connectionSemaphore.WaitAsync(cancellationToken);

                    try
                    {
                        TcpClient tcpClient = await listener.AcceptTcpClientAsync(cancellationToken);
                        _ = Task.Run(() => HandleConnectionSafelyAsync(tcpClient, cancellationToken));
                    }
                    catch
                    {
                        // If Accept fails or is cancelled, release the slot immediately
                        _connectionSemaphore.Release();
                        throw;
                    }
                }
            }
            catch(OperationCanceledException)
            {
                Console.WriteLine("[Gateway] SSH Server listener is shutting down smoothly");
            }
            finally
            {
                listener.Stop();
            }
        }

        private async Task HandleConnectionSafelyAsync(TcpClient tcpClient, CancellationToken cancellationToken)
        {
            try
            {
                using (tcpClient)
                {
                    await HandleConnectionAsync(tcpClient, cancellationToken);
                }
            }
            catch (Exception)
            {
                Console.WriteLine($"[Gateway] Error handling connection from {tcpClient.Client?.RemoteEndPoint}");
            }
            finally
            {
                // ALWAYS release the slot back to the bouncer when the guest leaves
                _connectionSemaphore.Release();
                Console.WriteLine($"[Gateway] Slot released. Active connections: {_settings.MaxConnections - _connectionSemaphore.CurrentCount}");
            }
        }

        private async Task HandleConnectionAsync(TcpClient tcpClient, CancellationToken cancellationToken)
        {
            try
            {
                Console.WriteLine($"[Gateway] New connection from {tcpClient.Client.RemoteEndPoint}");

                // 1. Define or use the default session algorithms
                var config = SshSessionConfiguration.Default;

                // 2. Instantiate a .NET TraceSource for protoco debugging
                var trace = new TraceSource("SshServerLogger", SourceLevels.Information);

                // Optional but can be helpful to see the logs: Direct trace Logs to terminal console
                trace.Listeners.Add(new ConsoleTraceListener());

                // 3. Initialize SshServerSession
                using var session = new SshServerSession(config, trace);
                var hostKey = await _hostKeyService.GetHostKeyAsync();
                session.Credentials = new[] { hostKey };

                // 4. Hook up Session-level Authentication
                session.Authenticating += (sender, e) =>
                {
                    _authenticationService.Authenticate(sender, e);
                };

                // 5. Hook up incoming Interactive Terminal request bindings
                session.ChannelOpening += async (sender, e) => {
                    if (e.Channel.ChannelType == "session")
                    {
                        // Call AcceptChannelAsync directly on the event argument:
                        SshChannel channel = await e.Channel.Session.AcceptChannelAsync(cancellationToken);

                        // Hand off the open channel to the terminal worker
                        _ = Task.Run(() => HandleShellChannelAsync(channel, cancellationToken));
                    }
                };

                // 6. TCP stream to an SSH Session
                using var stream = tcpClient.GetStream();
                await session.ConnectAsync(stream, cancellationToken);

                Console.WriteLine($"[Gateway] SSH Session negotiated for user: {session.Principal?.Identity?.Name}");

                // 7. Keep the async worker thread alive until client disconnects or token cancels
                var sessionClosedTcs = new TaskCompletionSource<bool>();
                session.Closed += (sender, e) => sessionClosedTcs.TrySetResult(true);

                using (cancellationToken.Register(() => sessionClosedTcs.TrySetResult(false)))
                {
                    await sessionClosedTcs.Task;
                }

                Console.WriteLine("[Gateway] Connection closed cleanly.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Gateway] Failed to accept incoming channel: {ex.Message}");
            }
        }

        private void HandleShellChannelAsync(SshChannel channel, CancellationToken cancellationToken)
        {
            channel.Request += async (sender, e) =>
            {

                if(e.RequestType == "pty-req" || e.RequestType == "env")
                {
                    // Create an empty success response message
                    Console.WriteLine("[Gateway] Success from HandleShellChannelAsyncMethod");
                    e.IsAuthorized = true;
                }
                else if(e.RequestType == "shell")
                {
                    e.IsAuthorized = true;

                    // Fire off interactive process loop in a background thread
                    //_ = Task.Run(() => RunInteractiveProcessAsync(channel));
                    _ = Task.Run(() => _tuiEngine.RunAsync(channel, cancellationToken));

                    Console.WriteLine("[Gateway] Acknowledge success from HandleShellChannelAsyncMethod");
                }
                else
                {
                    e.IsAuthorized = false;
                    Console.WriteLine("[Gateway] Rejected: unandled or unknown request");
                }
            };
        }

        private async Task RunInteractiveProcessAsync(SshChannel channel)
        {
            // Wrap the channel inside an SshStream to expose standard ReadAsync / WriteAsync
            using var sshStream = new SshStream(channel);

            // Initialize a new OS background process instance
            using var process = new Process();

            // Configure the process start parameters dynamically based on the OS
            process.StartInfo.FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/bash";
            process.StartInfo.Arguments = OperatingSystem.IsWindows() ? "" : "-1";

            // Intercept standard I/O streams instead of letting them use the host
            process.StartInfo.RedirectStandardInput = true;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.UseShellExecute = false; // Required to allow stream redirect
            process.StartInfo.CreateNoWindow = true; // Hidden background execution

            // Launch the process; exit early if execution fails
            if (!process.Start()) return;

            // Create a cancellation engine boudn to the SSH Channel's lifecycle
            using var cts = new CancellationTokenSource();
            channel.Closed += (s, e) => cts.Cancel();

            // Task 1 - Read from the SSH Client Network and Write to the Process Input
            var incoming = Task.Run(async () =>
            {
                byte[] buffer = new byte[1024]; // 4KB chunk buffer allocation
                while (!cts.Token.IsCancellationRequested)
                {
                    // Read incoming keystrokes/command from the SSH client network channel
                    int read = await sshStream.ReadAsync(buffer, 0, buffer.Length, cts.Token);
                    if (read <= 0) break;
                    
                    // DEBUG LOG: See if the server is actually getting your keystrokes
                    Console.WriteLine($"[Debug] Received {read} bytes from SSH client.");
                    await process.StandardInput.BaseStream.WriteAsync(buffer, 0, read, cts.Token);
                    await process.StandardInput.BaseStream.FlushAsync(cts.Token);
                }
            });

            // Task 2 - Read from the OS Process Output and Writeback to the ssh stream
            var outgoing = Task.Run(async () =>
            {
                byte[] buffer = new byte[4096];
                while (!cts.Token.IsCancellationRequested)
                {
                    int read = await process.StandardOutput.BaseStream.ReadAsync(buffer, 0, buffer.Length, cts.Token);
                    if (read <= 0) break;
                    

                    await sshStream.WriteAsync(buffer, 0, read, cts.Token);
                    await sshStream.FlushAsync(cts.Token);
                }
            });

            var errorPump = Task.Run(async () =>
            {
                byte[] buffer = new byte[4096];
                while (!cts.Token.IsCancellationRequested)
                {
                    int read = await process.StandardError.BaseStream.ReadAsync(buffer, 0, buffer.Length, cts.Token);
                    if (read <= 0) break;

                    await sshStream.WriteAsync(buffer, 0, read, cts.Token);
                    await sshStream.FlushAsync(cts.Token);
                }
            });

            await Task.WhenAny(incoming, outgoing, errorPump, process.WaitForExitAsync(cts.Token));
            if (!process.HasExited) process.Kill();
        }
    }
}
