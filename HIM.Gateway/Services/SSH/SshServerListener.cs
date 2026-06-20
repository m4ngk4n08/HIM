using HIM.Gateway.Models;
using HIM.Gateway.Services.SSH.Interfaces;
using Microsoft.DevTunnels.Ssh;
using Microsoft.DevTunnels.Ssh.Messages;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
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

        // Thread-safe tracking of active connections per IP and connection rate limiting history
        private readonly ConcurrentDictionary<string, int> _activeConnectionsPerIp = new();
        private readonly ConcurrentDictionary<string, List<DateTime>> _connectionHistory = new();
        private readonly object _historyLock = new();

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
                while (!cancellationToken.IsCancellationRequested)
                {
                    // Wait for an available slot before accepting a new connection
                    await _connectionSemaphore.WaitAsync(cancellationToken);

                    TcpClient? tcpClient = null;
                    try
                    {
                        tcpClient = await listener.AcceptTcpClientAsync(cancellationToken);

                        if (!IsIpAllowedAndTrack(tcpClient, out string ipAddress))
                        {
                            tcpClient.Close();
                            _connectionSemaphore.Release(); // Release slot because we rejected the IP
                            continue;
                        }

                        _ = Task.Run(() => HandleConnectionSafelyAsync(tcpClient, ipAddress, cancellationToken));
                    }
                    catch (OperationCanceledException)
                    {
                        _connectionSemaphore.Release();
                        throw;
                    }
                    catch (Exception ex)
                    {
                        // Resilient protection: log socket accept issues without crashing the entire server
                        Console.WriteLine($"[Gateway] Warning: Failed to accept TCP connection: {ex.Message}");
                        _connectionSemaphore.Release();
                        if (tcpClient != null)
                        {
                            try { tcpClient.Close(); } catch { }
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("[Gateway] SSH Server listener is shutting down smoothly");
            }
            finally
            {
                listener.Stop();
            }
        }

        private bool IsIpAllowedAndTrack(TcpClient tcpClient, out string ipAddress)
        {
            ipAddress = string.Empty;
            try
            {
                var remoteEndPoint = tcpClient.Client?.RemoteEndPoint as IPEndPoint;
                if (remoteEndPoint == null)
                {
                    return false;
                }

                ipAddress = remoteEndPoint.Address.ToString();
                var now = DateTime.UtcNow;

                // 1. Sliding window rate limit (Max 10 connections per 60 seconds per IP)
                lock (_historyLock)
                {
                    if (!_connectionHistory.TryGetValue(ipAddress, out var history))
                    {
                        history = new List<DateTime>();
                        _connectionHistory[ipAddress] = history;
                    }

                    // Prune old attempts
                    history.RemoveAll(t => (now - t).TotalSeconds > 60);

                    if (history.Count >= 10)
                    {
                        Console.WriteLine($"[Security] IP {ipAddress} rate limited (10 attempts/min exceeded).");
                        return false;
                    }

                    history.Add(now);
                }

                // 2. Concurrent connections limit (Max 3 active concurrent connections per IP)
                int currentActive = _activeConnectionsPerIp.AddOrUpdate(ipAddress, 1, (key, val) => val + 1);
                if (currentActive > 3)
                {
                    DecrementActiveConnection(ipAddress);
                    Console.WriteLine($"[Security] IP {ipAddress} rejected (Too many concurrent connections: {currentActive - 1} active).");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Gateway] Error during IP validation: {ex.Message}");
                return false;
            }
        }

        private void DecrementActiveConnection(string ipAddress)
        {
            if (string.IsNullOrEmpty(ipAddress)) return;
            _activeConnectionsPerIp.AddOrUpdate(ipAddress, 0, (key, val) => Math.Max(0, val - 1));
        }

        private async Task HandleConnectionSafelyAsync(TcpClient tcpClient, string ipAddress, CancellationToken cancellationToken)
        {
            try
            {
                using (tcpClient)
                {
                    await HandleConnectionAsync(tcpClient, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Gateway] Error handling connection from {ipAddress}: {ex.Message}");
            }
            finally
            {
                // Clean up IP tracking and release slot back to the semaphore
                DecrementActiveConnection(ipAddress);
                _connectionSemaphore.Release();
                Console.WriteLine($"[Gateway] Slot released for {ipAddress}. Active connections: {_settings.MaxConnections - _connectionSemaphore.CurrentCount}");
            }
        }

        private async Task HandleConnectionAsync(TcpClient tcpClient, CancellationToken cancellationToken)
        {
            try
            {
                Console.WriteLine($"[Gateway] New connection from {tcpClient.Client.RemoteEndPoint}");

                var config = SshSessionConfiguration.Default;
                var trace = new TraceSource("SshServerLogger", SourceLevels.Information);
                trace.Listeners.Add(new ConsoleTraceListener());

                // LINK token source BEFORE session init to enforce safe reverse-disposal order
                using var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                using var session = new SshServerSession(config, trace);
                var hostKey = await _hostKeyService.GetHostKeyAsync();
                session.Credentials = new[] { hostKey };

                // Handle session closure safely and prevent ObjectDisposedException
                EventHandler<Microsoft.DevTunnels.Ssh.Events.SshSessionClosedEventArgs> closedHandler = (sender, e) =>
                {
                    try
                    {
                        if (!sessionCts.IsCancellationRequested)
                        {
                            sessionCts.Cancel();
                        }
                    }
                    catch (ObjectDisposedException) { }
                };
                session.Closed += closedHandler;

                session.Authenticating += (sender, e) =>
                {
                    _authenticationService.Authenticate(sender, e);
                };

                session.ChannelOpening += async (sender, e) => {
                    if (e.Channel.ChannelType == "session")
                    {
                        SshChannel channel = await e.Channel.Session.AcceptChannelAsync(sessionCts.Token);
                        _ = Task.Run(() => HandleShellChannelAsync(channel, sessionCts.Token));
                    }
                };

                using var stream = tcpClient.GetStream();
                await session.ConnectAsync(stream, sessionCts.Token);

                Console.WriteLine($"[Gateway] SSH Session negotiated for user: {session.Principal?.Identity?.Name}");

                var sessionClosedTcs = new TaskCompletionSource<bool>();
                session.Closed += (sender, e) => sessionClosedTcs.TrySetResult(true);

                await sessionClosedTcs.Task;

                session.Closed -= closedHandler;
                Console.WriteLine("[Gateway] Connection closed cleanly.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Gateway] Session exception: {ex.Message}");
            }
        }

        private void HandleShellChannelAsync(SshChannel channel, CancellationToken sessionToken)
        {
            var channelCts = CancellationTokenSource.CreateLinkedTokenSource(sessionToken);
            channel.Closed += (s, e) => channelCts.Cancel();

            uint terminalWidth = 80;
            uint terminalHeight = 24;
            channel.Request += (sender, e) =>
            {
                switch (e.RequestType)
                {
                    case "pty-req":
                        e.IsAuthorized = true;
                        var ptyMsg = e.Request.ConvertTo<TerminalRequestMessage>();
                        if (ptyMsg != null)
                        {
                            terminalWidth = ptyMsg.Columns;
                            terminalHeight = ptyMsg.Rows;
                        }
                        break;
                    case "window-change":
                        e.IsAuthorized = true;
                        Console.WriteLine($"[Gateway] Received window-change request from {channel.Session.Principal?.Identity?.Name}. Feature currently parked.");
                        break;

                    case "shell":
                        e.IsAuthorized = true;
                        if (e.RequestType == "shell")
                            _ = Task.Run(() => _tuiEngine.RunAsync(channel, terminalWidth, terminalHeight, channelCts.Token));
                        break;
                    default:
                        e.IsAuthorized = false;
                        Console.WriteLine($"[Security] Rejected unauthorized {e.RequestType} request from {channel.Session.Principal?.Identity?.Name}");
                        break;
                }
            };
        }
    }
}