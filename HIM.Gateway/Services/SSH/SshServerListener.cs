using HIM.Gateway.Models;
using HIM.Gateway.Services.SSH.Interfaces;
using Microsoft.DevTunnels.Ssh;
using Microsoft.DevTunnels.Ssh.Messages;
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

                // !Adjustments: Precise Lifecycle Management: we will introduce a CancellationTokenSource linked to the session's lifecycle. This ensures that when a user disconnects, all background task(AI streaming, input loops) are terminated immidiately, preventing "Zombie Tasks."
                using var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                session.Closed += (sender, e) => sessionCts.Cancel();

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
                        SshChannel channel = await e.Channel.Session.AcceptChannelAsync(sessionCts.Token);

                        // Hand off the open channel to the terminal worker
                        _ = Task.Run(() => HandleShellChannelAsync(channel, sessionCts.Token));
                    }
                };

                // 6. TCP stream to an SSH Session
                using var stream = tcpClient.GetStream();
                await session.ConnectAsync(stream, sessionCts.Token);

                Console.WriteLine($"[Gateway] SSH Session negotiated for user: {session.Principal?.Identity?.Name}");

                // 7. Keep the async worker thread alive until client disconnects or token cancels
                var sessionClosedTcs = new TaskCompletionSource<bool>();
                session.Closed += (sender, e) => sessionClosedTcs.TrySetResult(true);

                await sessionClosedTcs.Task;

                Console.WriteLine("[Gateway] Connection closed cleanly.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Gateway] Failed to accept incoming channel: {ex.Message}");
            }
        }

        private void HandleShellChannelAsync(SshChannel channel, CancellationToken sessionToken)
        {
            // Create a channel-scoped token linked to the session
            var channelCts = CancellationTokenSource.CreateLinkedTokenSource(sessionToken);
            channel.Closed += (s, e) => channelCts.Cancel();

            uint terminalWidth = 80;
            uint terminalHeight = 24;
            channel.Request += (sender, e) =>
            {
                // SECURITY: Only allow terminal-related requests.
                // Explicitly reject 'exec' and 'env' to prevent bypass attempts.
                switch (e.RequestType)
                {
                    case "pty-req":
                        e.IsAuthorized = true;
                        // pty-req HAS the 'TERM' string, so ConvertTo works here
                        var ptyMsg = e.Request.ConvertTo<TerminalRequestMessage>();
                        if (ptyMsg != null)
                        {
                            terminalWidth = ptyMsg.Columns;
                            terminalHeight = ptyMsg.Rows;
                        }
                        break;
                    case "window-change":
                        // [BUG] Window resizing is currently parked due to library-specific parsing issues.
                        // We authorize the request to prevent client errors, but do not update dimensions.
                        e.IsAuthorized = true;
                        Console.WriteLine($"[Gateway] Received window-change request from {channel.Session.Principal?.Identity.Name}. Feature currently parked.");
                        break;

                    case "shell":
                        e.IsAuthorized = true;
                        if (e.RequestType == "shell")
                            _ = Task.Run(() => _tuiEngine.RunAsync(channel, terminalWidth, terminalHeight, channelCts.Token));
                        break;
                    default:
                        e.IsAuthorized = false;
                        Console.WriteLine($"[Security] Rejected unauthorized {e.RequestType} request from {channel.Session.Principal?.Identity.Name}");
                        break;
                }
            };
        }

    }
}
