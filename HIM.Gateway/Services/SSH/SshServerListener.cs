using HIM.Gateway.Models;
// NOTE: Idle timeout is intentionally handled inside ConsoleEngineService.HandleInteractionLoopAsync
// via a per-read CancelAfter that resets on every user keystroke. Do NOT add a session-level
// CancelAfter here — it would fire unconditionally and kill active sessions mid-write.
using HIM.Gateway.Services.SSH.Interfaces;
using Microsoft.DevTunnels.Ssh;
using Microsoft.DevTunnels.Ssh.Messages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace HIM.Gateway.Services.SSH
{
    /// <summary>
    /// TCP/SSH gateway with 8-layer bot defense.
    ///
    /// Defense layers (in order of evaluation — cheapest checks first):
    /// ──────────────────────────────────────────────────────────────────
    ///  Layer 1 │ IP BanList          │ Lock-free ConcurrentDictionary read
    ///  Layer 2 │ Tarpit on reject    │ Bounded concurrent tarpitting thread guard
    ///  Layer 3 │ Global flood guard  │ CAS-based token bucket, zero locks
    ///  Layer 4 │ Per-IP rate limit   │ Lock-free sliding window via ConcurrentQueue sharding
    ///  Layer 5 │ Per-IP concurrency  │ Interlocked counter via ConcurrentDictionary
    ///  Layer 6 │ Handshake Timeout   │ Linked CancellationTokenSource with CancelAfter
    ///  Layer 7 │ Negotiation Timeout │ Linked countdown enforcing shell channel request
    ///  Layer 8 │ Idle timeout        │ Linked CancellationTokenSource with CancelAfter (TUI)
    /// </summary>
    public class SshServerListener : ISshServerListener
    {
        // ── Tuning Constants ──────────────────────────────────────────────
        private const int HandshakeTimeoutSeconds = 15;
        private const int NegotiationTimeoutSeconds = 15;
        private const int MaxConcurrentTarpits = 100; // Hard cap to prevent Thread Pool exhaustion

        // ── Injected Dependencies ─────────────────────────────────────────
        private readonly ITuiEngine _tuiEngine;
        private readonly IHostKeyService _hostKeyService;
        private readonly IAuthenticationService _authenticationService;
        private readonly IIpBanService _ipBanService;
        private readonly ILogger<SshServerListener> _logger;
        private readonly SshSettings _settings;

        // ── Global Semaphore (bounds total concurrent SSH sessions) ────────
        private readonly SemaphoreSlim _connectionSemaphore;

        // ── Per-IP Tracking ───────────────────────────────────────────────
        // Active concurrent connection count per IP. Interlocked via AddOrUpdate.
        private readonly ConcurrentDictionary<string, int> _activeConnectionsPerIp = new();

        // Lock-Free Sliding-window history per IP. Eliminates global lock contention under DDoS.
        private readonly ConcurrentDictionary<string, ConcurrentQueue<DateTime>> _connectionHistory = new();

        // ── Global Flood Guard — Lock-free Token Bucket ───────────────────
        // _floodWindowStart: TickCount64 of when the current 1-second window began.
        // _floodWindowCount: number of connections admitted in the current window.
        // Both are manipulated exclusively through Interlocked/CAS operations.
        private long _floodWindowStart = Environment.TickCount64;
        private int _floodWindowCount;

        // ── Bounded Tarpit Tracking ───────────────────────────────────────
        private int _activeTarpits;

        // ── Periodic History Pruning (memory safety) ──────────────────────
        private int _acceptCounter;
        private const int PruneHistoryEvery = 500; // every 500 accepted connections

        // ── Constructor ───────────────────────────────────────────────────

        public SshServerListener(
            ITuiEngine tuiEngine,
            IHostKeyService hostKeyService,
            IAuthenticationService authenticationService,
            IIpBanService ipBanService,
            ILogger<SshServerListener> logger,
            IOptions<SshSettings> settings)
        {
            _tuiEngine = tuiEngine;
            _hostKeyService = hostKeyService;
            _authenticationService = authenticationService;
            _ipBanService = ipBanService;
            _logger = logger;
            _settings = settings.Value;
            _connectionSemaphore = new SemaphoreSlim(_settings.MaxConnections, _settings.MaxConnections);
        }

        // ── Public API ────────────────────────────────────────────────────

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            var listener = new TcpListener(IPAddress.Any, _settings.Port);
            listener.Start();

            _logger.LogInformation(
                "[Gateway] {Timestamp:yyyy-MM-dd HH:mm:ss} UTC | SSH listener started | " +
                "Port: {Port} | MaxConnections: {Max} | IdleTimeout: {Idle}m",
                DateTime.UtcNow, _settings.Port, _settings.MaxConnections, _settings.IdleTimeoutSeconds);

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    // Block until a connection slot is available.
                    // This naturally backpressures when the server is at capacity.
                    await _connectionSemaphore.WaitAsync(cancellationToken);

                    TcpClient? tcpClient = null;
                    string ipAddress = string.Empty;

                    try
                    {
                        tcpClient = await listener.AcceptTcpClientAsync(cancellationToken);

                        // Resolve IP immediately — all subsequent checks require it.
                        var remoteEndPoint = tcpClient.Client?.RemoteEndPoint as IPEndPoint;
                        if (remoteEndPoint == null)
                        {
                            _logger.LogWarning("[Gateway] {Timestamp:yyyy-MM-dd HH:mm:ss} UTC | Rejected connection: no remote endpoint.",
                                DateTime.UtcNow);
                            ReleaseAndClose(tcpClient);
                            continue;
                        }
                        ipAddress = remoteEndPoint.Address.ToString();

                        // ─── Layer 3: Global Flood Guard (cheapest, check first) ───
                        if (!TryConsumeGlobalSlot())
                        {
                            _logger.LogWarning(
                                "[Security] {Timestamp:yyyy-MM-dd HH:mm:ss} UTC | GLOBAL FLOOD LIMIT | " +
                                "Rejected: {IpAddress} | Limit: {Limit}/sec",
                                DateTime.UtcNow, ipAddress, _settings.MaxGlobalConnectionsPerSecond);
                            _connectionSemaphore.Release();
                            TarpitAndReject(tcpClient, ipAddress, "GlobalFloodLimit");
                            continue;
                        }

                        // ─── Layer 1: IP Ban Check (lock-free read) ─────────────────
                        if (_ipBanService.IsBanned(ipAddress))
                        {
                            _logger.LogWarning(
                                "[Security] {Timestamp:yyyy-MM-dd HH:mm:ss} UTC | BANNED IP | " +
                                "Rejected: {IpAddress}",
                                DateTime.UtcNow, ipAddress);
                            _connectionSemaphore.Release();
                            TarpitAndReject(tcpClient, ipAddress, "Banned");
                            continue;
                        }

                        // ─── Layers 4 & 5: Per-IP Rate Limit + Concurrent Limit ─────
                        if (!IsAllowedAndTrack(ipAddress))
                        {
                            // Strike recording is done inside IsAllowedAndTrack.
                            _connectionSemaphore.Release();
                            TarpitAndReject(tcpClient, ipAddress, "RateOrConcurrentLimit");
                            continue;
                        }

                        // Trigger periodic history prune (non-blocking, on thread pool)
                        if (Interlocked.Increment(ref _acceptCounter) % PruneHistoryEvery == 0)
                            _ = Task.Run(PruneConnectionHistory);

                        // Configure OS Socket-Level TCP Keep-Alives (Dead Connection Reclamation)
                        ConfigureSocketKeepAlive(tcpClient.Client);

                        // All checks passed — hand off to connection handler.
                        _ = Task.Run(() => HandleConnectionSafelyAsync(tcpClient, ipAddress, cancellationToken),
                            cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        _connectionSemaphore.Release();
                        if (tcpClient != null) try { tcpClient.Close(); } catch { }
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex,
                            "[Gateway] {Timestamp:yyyy-MM-dd HH:mm:ss} UTC | Accept error: {Message}",
                            DateTime.UtcNow, ex.Message);
                        _connectionSemaphore.Release();
                        if (tcpClient != null) try { tcpClient.Close(); } catch { }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("[Gateway] {Timestamp:yyyy-MM-dd HH:mm:ss} UTC | SSH listener shutting down gracefully.",
                    DateTime.UtcNow);
            }
            finally
            {
                listener.Stop();
            }
        }

        // ── Layer 3: Global Flood Guard ───────────────────────────────────

        /// <summary>
        /// Lock-free sliding per-second token bucket.
        /// Uses CAS on _floodWindowStart to elect a single "window-reset winner"
        /// among concurrent threads. All losers proceed to the Interlocked.Increment
        /// path which is also lock-free. Worst-case: BanDurationMinutes+1 connections
        /// in a window boundary race — acceptable overage for a coarse global guard.
        /// </summary>
        private bool TryConsumeGlobalSlot()
        {
            var now = Environment.TickCount64;
            var windowStart = Interlocked.Read(ref _floodWindowStart);

            if (now - windowStart >= 1_000) // 1-second window expired
            {
                // CAS: only one thread resets the window; others fall through.
                if (Interlocked.CompareExchange(ref _floodWindowStart, now, windowStart) == windowStart)
                {
                    Interlocked.Exchange(ref _floodWindowCount, 1);
                    return true; // window-reset thread always gets a slot
                }
            }

            return Interlocked.Increment(ref _floodWindowCount) <= _settings.MaxGlobalConnectionsPerSecond;
        }

        // ── Layers 4 & 5: Per-IP Rate Limit + Concurrent Limit ───────────

        /// <summary>
        /// Returns true if the IP is allowed to proceed; false if it violated
        /// either the sliding-window rate limit or the concurrent connection cap.
        /// On failure, a strike is recorded in IpBanService.
        /// Optimized to use lock-free queues, eliminating lock contention bottlenecks under DDoS.
        /// </summary>
        private bool IsAllowedAndTrack(string ipAddress)
        {
            var now = DateTime.UtcNow;

            // ── Layer 4: Sliding-window rate limit (Lock-Free) ────────────
            var history = _connectionHistory.GetOrAdd(ipAddress, _ => new ConcurrentQueue<DateTime>());

            // Prune old entries out of the queue snapshot asynchronously
            var cutoff = now.AddSeconds(-_settings.RateLimitWindowSeconds);
            while (history.TryPeek(out var oldest) && oldest < cutoff)
            {
                history.TryDequeue(out _);
            }

            if (history.Count >= _settings.RateLimitMaxAttempts)
            {
                _ipBanService.RecordStrike(ipAddress);
                _logger.LogWarning(
                    "[Security] {Timestamp:yyyy-MM-dd HH:mm:ss} UTC | RATE LIMIT | " +
                    "IP: {IpAddress} | Attempts: {Count}/{Max} in {Window}s",
                    DateTime.UtcNow, ipAddress,
                    history.Count, _settings.RateLimitMaxAttempts, _settings.RateLimitWindowSeconds);
                return false;
            }

            history.Enqueue(now);

            // ── Layer 5: Concurrent connection cap ────────────────────────
            int active = _activeConnectionsPerIp.AddOrUpdate(ipAddress, 1, (_, val) => val + 1);
            if (active > _settings.MaxConcurrentPerIp)
            {
                DecrementActiveConnection(ipAddress);
                _ipBanService.RecordStrike(ipAddress);
                _logger.LogWarning(
                    "[Security] {Timestamp:yyyy-MM-dd HH:mm:ss} UTC | CONCURRENT LIMIT | " +
                    "IP: {IpAddress} | Active: {Active}/{Max}",
                    DateTime.UtcNow, ipAddress, active, _settings.MaxConcurrentPerIp);
                return false;
            }

            return true;
        }

        // ── Layer 2: Tarpit ───────────────────────────────────────────────

        /// <summary>
        /// Holds a rejected socket open for TarpitDelayMs before closing it.
        /// Bounded via Interlocked counter. Prevents memory exhaustion attacks
        /// from spawning millions of idle Delay tasks under volumetric connection flooding.
        /// </summary>
        private void TarpitAndReject(TcpClient client, string ipAddress, string reason)
        {
            // Safeguard: If the concurrent tarpit count exceeds safety levels, bypass the delay and reject immediately.
            if (Interlocked.Increment(ref _activeTarpits) > MaxConcurrentTarpits)
            {
                Interlocked.Decrement(ref _activeTarpits);
                _logger.LogWarning(
                    "[Security] {Timestamp:yyyy-MM-dd HH:mm:ss} UTC | TARPIT BYPASS | " +
                    "IP: {IpAddress} | Reason: {Reason} | Max concurrent tarpits reached. Dropping connection immediately.",
                    DateTime.UtcNow, ipAddress, reason);
                try { client.Close(); } catch { }
                return;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    _logger.LogWarning(
                        "[Security] {Timestamp:yyyy-MM-dd HH:mm:ss} UTC | TARPIT | " +
                        "IP: {IpAddress} | Reason: {Reason} | Delay: {DelayMs}ms",
                        DateTime.UtcNow, ipAddress, reason, _settings.TarpitDelayMs);

                    await Task.Delay(_settings.TarpitDelayMs);
                }
                finally
                {
                    Interlocked.Decrement(ref _activeTarpits);
                    try { client.Close(); } catch { /* socket already closed — ignore */ }
                }
            });
        }

        // ── Connection Lifecycle ──────────────────────────────────────────

        private async Task HandleConnectionSafelyAsync(
            TcpClient client, string ipAddress, CancellationToken cancellationToken)
        {
            try
            {
                using (client)
                {
                    await HandleConnectionAsync(client, ipAddress, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[Gateway] {Timestamp:yyyy-MM-dd HH:mm:ss} UTC | Connection error | IP: {IpAddress} | {Message}",
                    DateTime.UtcNow, ipAddress, ex.Message);
            }
            finally
            {
                DecrementActiveConnection(ipAddress);
                _connectionSemaphore.Release();
                _logger.LogInformation(
                    "[Gateway] {Timestamp:yyyy-MM-dd HH:mm:ss} UTC | Slot released | IP: {IpAddress} | " +
                    "Active connections: {Active}/{Max}",
                    DateTime.UtcNow, ipAddress,
                    _settings.MaxConnections - _connectionSemaphore.CurrentCount,
                    _settings.MaxConnections);
            }
        }

        private async Task HandleConnectionAsync(
            TcpClient client, string ipAddress, CancellationToken cancellationToken)
        {
            _logger.LogInformation(
                "[Gateway] {Timestamp:yyyy-MM-dd HH:mm:ss} UTC | New connection | IP: {IpAddress}",
                DateTime.UtcNow, ipAddress);

            var config = SshSessionConfiguration.Default;
            var trace = new TraceSource("SshServerLogger", SourceLevels.Warning);
            trace.Listeners.Add(new ConsoleTraceListener());

            // ── Layer 8: Idle Timeout (owned by ConsoleEngineService) ───────────
            // The actual idle timeout — which resets on EVERY user keystroke — is
            // implemented inside ConsoleEngineService.HandleInteractionLoopAsync via
            // a per-read CancelAfter. This session-level CTS is intentionally NOT
            // given a CancelAfter. It only fires on:
            //   (a) Host shutdown (cancellationToken)
            //   (b) Client disconnect (session.Closed event)
            // Adding CancelAfter here would create a wall-clock timer that kills
            // active sessions mid-write and causes "Cannot send more data after EOF".
            using var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            using var session = new SshServerSession(config, trace);
            var hostKey = await _hostKeyService.GetHostKeyAsync();
            session.Credentials = new[] { hostKey };

            // Wire session closure → sessionCts so all downstream tasks
            // (channel handlers, TUI engine) are cancelled when the client disconnects.
            session.Closed += (_, _) =>
            {
                try { if (!sessionCts.IsCancellationRequested) sessionCts.Cancel(); }
                catch (ObjectDisposedException) { /* race with using-block disposal — safe to ignore */ }
            };

            session.Authenticating += (sender, e) => _authenticationService.Authenticate(sender, e);

            // ── Layer 7: Pre-shell Negotiation Timeout ───────────────────────
            // This timer ensures that if a bot negotiates a session but never starts the TUI shell,
            // we forcibly disconnect them after 15 seconds. This prevents bots from holding open sessions 
            // indefinitely without actually using them.
            using var negotiationCts = CancellationTokenSource.CreateLinkedTokenSource(sessionCts.Token);
            negotiationCts.CancelAfter(TimeSpan.FromSeconds(NegotiationTimeoutSeconds));

            session.ChannelOpening += async (sender, e) =>
            {
                if (e.Channel.ChannelType == "session")
                {
                    var channel = await e.Channel.Session.AcceptChannelAsync(sessionCts.Token);
                    // Pass the negotiation CTS down to the shell handler so it can be disarmed upon shell launch
                    _ = Task.Run(() => HandleShellChannelAsync(channel, ipAddress, sessionCts.Token, negotiationCts));
                }
            };

            using var stream = client.GetStream();

            // ── Layer 6: Handshake-Specific Disarmable Timeout ─────────────────
            // Create a dedicated CTS for the handshake phase, linked to the main session token.
            using var handshakeCts = CancellationTokenSource.CreateLinkedTokenSource(sessionCts.Token);
            // Enforce a strict 15-second timeout limit for the cryptographic SSH handshake.
            handshakeCts.CancelAfter(TimeSpan.FromSeconds(HandshakeTimeoutSeconds));

            try
            {
                // Pass the handshake-specific token to ConnectAsync.
                await session.ConnectAsync(stream, handshakeCts.Token);

                // DISARM: Handshake completed successfully. Disable the timeout 
                // immediately so the ongoing session can run indefinitely.
                handshakeCts.CancelAfter(Timeout.InfiniteTimeSpan);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // If the host was not shutting down, the cancellation originated from the handshake timer.
                throw new TimeoutException($"SSH handshake timed out after {HandshakeTimeoutSeconds} seconds.");
            }

            _logger.LogInformation(
                "[Gateway] {Timestamp:yyyy-MM-dd HH:mm:ss} UTC | Session negotiated | " +
                "IP: {IpAddress} | User: {User}",
                DateTime.UtcNow, ipAddress, session.Principal?.Identity?.Name ?? "unknown");

            var sessionClosedTcs = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            session.Closed += (_, _) => sessionClosedTcs.TrySetResult(true);

            // Monitor the pre-shell countdown. If the 15 seconds expires before the shell is launched, close the session.
            using var negotiationReg = negotiationCts.Token.Register(() =>
            {
                if (!sessionCts.Token.IsCancellationRequested && !sessionClosedTcs.Task.IsCompleted)
                {
                    _logger.LogWarning(
                        "[Security] {Timestamp:yyyy-MM-dd HH:mm:ss} UTC | SESSION IDLE TIMEOUT | " +
                        "Forcibly disconnecting idle session before shell launch | IP: {IpAddress}",
                        DateTime.UtcNow, ipAddress);
                    _ = session.CloseAsync(SshDisconnectReason.ByApplication, "Session idle timeout before shell execution.");
                }
            });

            // If the host shuts down (Ctrl+C / SIGINT) while the session is open,
            // cancel the TCS so HandleConnectionAsync unblocks and cleans up.
            using var hostShutdownReg = sessionCts.Token.Register(
                () => sessionClosedTcs.TrySetCanceled());

            try
            {
                await sessionClosedTcs.Task;
                _logger.LogInformation(
                    "[Gateway] {Timestamp:yyyy-MM-dd HH:mm:ss} UTC | Session closed cleanly | IP: {IpAddress}",
                    DateTime.UtcNow, ipAddress);
            }
            catch (OperationCanceledException)
            {
                // Only reaches here on host shutdown, NOT idle timeout.
                // Idle timeout is handled inside ConsoleEngineService and propagates
                // as a clean session close (stream EOF), not a CancellationException here.
                _logger.LogInformation(
                    "[Gateway] {Timestamp:yyyy-MM-dd HH:mm:ss} UTC | Session cancelled (host shutdown) | IP: {IpAddress}",
                    DateTime.UtcNow, ipAddress);
            }
        }

        private void HandleShellChannelAsync(SshChannel channel, string ipAddress, CancellationToken sessionToken, CancellationTokenSource negotiationCts)
        {
            var channelCts = CancellationTokenSource.CreateLinkedTokenSource(sessionToken);
            channel.Closed += (_, _) => { try { channelCts.Cancel(); } catch { } };

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
                        _logger.LogDebug("[Gateway] window-change from {User}",
                            channel.Session.Principal?.Identity?.Name);
                        break;

                    case "shell":
                        e.IsAuthorized = true;

                        // DISARM: The shell is launching. Disable the 15-second countdown.
                        // Interactive keyboard idle timeout in TUI engine will now take over.
                        try { negotiationCts.CancelAfter(Timeout.InfiniteTimeSpan); } catch { }

                        if (!channelCts.IsCancellationRequested)  // <-- guard
                        {
                            _ = Task.Run(() => _tuiEngine.RunAsync(
                                channel, terminalWidth, terminalHeight, channelCts.Token));
                        }
                        break;

                    default:
                        e.IsAuthorized = false;

                        // Defend against log-injection attacks by sanitizing variables parsed directly from the network
                        var safeUser = SanitizeLogInput(channel.Session.Principal?.Identity?.Name);
                        var safeType = SanitizeLogInput(e.RequestType);

                        _logger.LogWarning(
                            "[Security] {Timestamp} | REJECTED request | IP: {IP} | Type: {Type} | User: {User}",
                            DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss 'UTC'"),
                            ipAddress,
                            safeType,
                            safeUser);

                        // Fire and forget a task that yields for 100ms.
                        // this gives the framework engough time to send the "Unauthorized" packet
                        // cleanly to the client before we teardown the SshSession.
                        try
                        {
                            _ = Task.Run(async () =>
                            {
                                await Task.Delay(100);
                                await channel.Session.CloseAsync(SshDisconnectReason.ByApplication, "Execution Rejected");

                            });
                        }
                        catch (Exception)
                        {
                            // Ignore safe cleanup exceptions
                        }
                        break;
                }
            };
        }

        // ── Private Utilities ─────────────────────────────────────────────

        /// <summary>
        /// Sanitizes strings parsed from network inputs before sending them to the logging pipeline,
        /// shielding Fail2Ban from CRLF spoofing or ANSI log injection attacks.
        /// </summary>
        private static string SanitizeLogInput(string? input, int maxLength = 50)
        {
            if (string.IsNullOrEmpty(input)) return "unknown";

            var truncated = input.Length > maxLength ? input.Substring(0, maxLength) : input;

            // Strip any control characters, carriage returns, and newlines
            var clean = Regex.Replace(truncated, @"[\p{C}\r\n]", string.Empty);

            // Strip ANSI color/terminal escape codes
            return Regex.Replace(clean, @"\x1B\[[^@-_]*[0-9a-zA-Z]", string.Empty);
        }

        /// <summary>
        /// Configures platform-independent aggressive TCP keep-alives at the OS-socket level.
        /// Reclaims "half-open" dead connections from silent network drops in minutes instead of hours.
        /// </summary>
        private void ConfigureSocketKeepAlive(Socket socket)
        {
            try
            {
                // 1. Enable TCP Keep-Alives on the socket
                socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

                // 2. Configure Keep-Alive parameters (Natively supported since .NET Core 3.0+)
                // Wait 60 seconds without activity before sending the first probe
                socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, 60);

                // Wait 10 seconds between subsequent unanswered probes
                socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, 10);

                // Terminate the connection after 5 consecutive failed probes
                socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, 5);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "[Gateway] Socket-level TCP Keep-Alive configuration failed. Falling back to OS defaults.");
            }
        }

        private void DecrementActiveConnection(string ipAddress)
        {
            if (string.IsNullOrEmpty(ipAddress)) return;
            _activeConnectionsPerIp.AddOrUpdate(ipAddress, 0, (_, val) => Math.Max(0, val - 1));
        }

        private void ReleaseAndClose(TcpClient? client)
        {
            _connectionSemaphore.Release();
            if (client != null) try { client.Close(); } catch { }
        }

        /// <summary>
        /// Removes IPs from the sliding-window history that have had no
        /// activity within the rate-limit window. Runs on the thread pool
        /// every PruneHistoryEvery accepted connections.
        /// Memory safety guarantee: dictionary size is bounded by the number
        /// of unique IPs seen within any given window, not cumulative lifetime.
        /// Lock-Free implementation using ConcurrentQueue structures.
        /// </summary>
        private void PruneConnectionHistory()
        {
            var cutoff = DateTime.UtcNow.AddSeconds(-_settings.RateLimitWindowSeconds);
            var toRemove = new List<string>();

            foreach (var (key, history) in _connectionHistory)
            {
                while (history.TryPeek(out var oldest) && oldest < cutoff)
                {
                    history.TryDequeue(out _);
                }
                if (history.IsEmpty) toRemove.Add(key);
            }

            foreach (var key in toRemove)
                _connectionHistory.TryRemove(key, out _);

            _logger.LogDebug(
                "[Gateway] {Timestamp:yyyy-MM-dd HH:mm:ss} UTC | History pruned | " +
                "Removed: {Removed} IPs | Tracked: {Tracked} IPs",
                DateTime.UtcNow, toRemove.Count, _connectionHistory.Count);
        }
    }
}