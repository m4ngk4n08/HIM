using HIM.Gateway.Models;
// NOTE: Idle timeout is intentionally handled inside ConsoleEngineService.HandleInteractionLoopAsync
// via a per-read CancelAfter that resets on every user keystroke. Do NOT add a session-level
// CancelAfter here — it would fire unconditionally and kill active sessions mid-write.
using HIM.Gateway.Services.SSH.Interfaces;
using HIM.Gateway.Services.SSH.Interfaces.IGates;
using HIM.Gateway.Services.SSH.Messages;
using Microsoft.DevTunnels.Ssh;
using Microsoft.DevTunnels.Ssh.Messages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace HIM.Gateway.Services.SSH
{
    /// <summary>
    /// TCP/SSH gateway with 8-layer bot defense.
    ///
    /// Only four of the eight layers are gate-shaped decisions (allow/reject at accept time);
    /// those four live in Services/SSH/Gates/ as IConnectionGate implementations, registered in
    /// ServiceExtensions.AddService in evaluation order. The other four are not gates — they are
    /// either the shared rejection action every gate feeds, or per-session cancellation tokens
    /// armed after the socket is already accepted:
    /// ──────────────────────────────────────────────────────────────────
    ///  Layer 1 │ IP BanList          │ Gates/IpBanGate.cs — Lock-free ConcurrentDictionary read
    ///  Layer 2 │ Tarpit on reject    │ SshServerListener.TarpitAndReject — the uniform rejection
    ///          │                     │ action every gate's result feeds; not itself a decision
    ///  Layer 3 │ Global flood guard  │ Gates/GlobalFloodGate.cs — CAS-based token bucket
    ///  Layer 4 │ Per-IP rate limit   │ Gates/PerIpRateGate.cs — Lock-free sliding window
    ///  Layer 5 │ Per-IP concurrency  │ Gates/PerIpConcurrencyGate.cs — Interlocked counter via
    ///          │                     │ ConcurrentDictionary; the only gate with acquire/release
    ///  Layer 6 │ Handshake Timeout   │ SshServerListener.HandleConnectionAsync — a linked CTS
    ///          │                     │ (handshakeCts) armed after accept, disarmed on success
    ///  Layer 7 │ Negotiation Timeout │ SshServerListener.HandleConnectionAsync — a linked CTS
    ///          │                     │ (negotiationCts) enforcing the shell channel request
    ///  Layer 8 │ Idle timeout        │ ConsoleEngineService.HandleInteractionLoopAsync — a
    ///          │                     │ per-read CancelAfter, in a different class entirely
    ///
    /// Gate evaluation order is L3, L1, L4, L5 (cheapest checks first) — see
    /// SshServerListener.EvaluateGates and
    /// ConnectionGatePipelineTests.RegistrationOrder_IsEvaluationOrder_L3ThenL1ThenL4ThenL5.
    /// </summary>
    public class SshServerListener : ISshServerListener
    {
        // ── Tuning Constants ──────────────────────────────────────────────
        private const int HandshakeTimeoutSeconds = 15;
        private const int NegotiationTimeoutSeconds = 15;
        private const int MaxConcurrentTarpits = 100; // Hard cap to prevent Thread Pool exhaustion

        // ── Injected Dependencies ─────────────────────────────────────────
        // ITuiEngine is intentionally NOT injected here: it (and everything it depends on)
        // is Scoped per session, and this listener is a Singleton. A scope is created per
        // shell channel in HandleShellChannelAsync and ITuiEngine is resolved from it there.
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly IHostKeyService _hostKeyService;
        private readonly IAuthenticationService _authenticationService;
        private readonly IReadOnlyList<IConnectionGate> _gates;
        private readonly IConnectionSlotGate _slotGate;
        private readonly ILogger<SshServerListener> _logger;
        private readonly SshSettings _settings;
        private readonly IConnectionMetricsService _metrics;
        private readonly ISessionRegistryService _sessionRegistry;

        // ── Global Semaphore (bounds total concurrent SSH sessions) ────────
        private readonly SemaphoreSlim _connectionSemaphore;

        // ── Bounded Tarpit Tracking ───────────────────────────────────────
        private int _activeTarpits;

        // ── Constructor ───────────────────────────────────────────────────

        public SshServerListener(
            IServiceScopeFactory serviceScopeFactory,
            IHostKeyService hostKeyService,
            IAuthenticationService authenticationService,
            IEnumerable<IConnectionGate> gates,
            IConnectionSlotGate slotGate,
            ILogger<SshServerListener> logger,
            IOptions<SshSettings> settings,
            IConnectionMetricsService metrics,
            ISessionRegistryService sessionRegistry)
        {
            _serviceScopeFactory = serviceScopeFactory;
            _hostKeyService = hostKeyService;
            _authenticationService = authenticationService;
            // Materialized once — registration order is evaluation order (see
            // ServiceExtensions.AddConnectionGate), and re-enumerating IEnumerable<T> on every
            // connection would re-run the DI resolution logic on the accept-loop hot path.
            _gates = gates.ToArray();
            _slotGate = slotGate;
            _logger = logger;
            _settings = settings.Value;
            _connectionSemaphore = new SemaphoreSlim(_settings.MaxConnections, _settings.MaxConnections);
            // Required, deliberately: an optional parameter with a private fallback would let a
            // missing DI registration slip past ValidateOnBuild, and the listener would then
            // silently count into a throwaway instance while /defense reported zeros forever with
            // nothing failing anywhere. A required parameter turns that into a startup error.
            _metrics = metrics;
            _sessionRegistry = sessionRegistry;
        }

        // ── Public API ────────────────────────────────────────────────────

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            var listener = new TcpListener(IPAddress.Any, _settings.Port);
            listener.Start();

            _logger.LogInformation(
                "[Gateway] {Timestamp:yyyy-MM-dd HH:mm:ss} UTC | SSH listener started | " +
                "Port: {Port} | MaxConnections: {Max} | IdleTimeout: {Idle}s",
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

                        // ─── Gate pipeline: L3, L1, L4, L5, in registration order ───
                        var gateResult = EvaluateGates(new ConnectionContext(ipAddress));
                        if (!gateResult.IsAllowed)
                        {
                            _connectionSemaphore.Release();
                            TarpitAndReject(tcpClient, ipAddress, gateResult.Reason!);
                            continue;
                        }

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

        /// <summary>
        /// Runs every registered gate in registration order. The first rejecting gate wins and
        /// later gates do not run — internal (rather than private) so
        /// ConnectionGatePipelineTests.FirstRejectingGate_Wins_AndLaterGatesDoNotRun exercises
        /// this exact short-circuit rather than a re-implementation of it.
        /// </summary>
        internal GateResult EvaluateGates(ConnectionContext ctx)
        {
            foreach (var gate in _gates)
            {
                var result = gate.Evaluate(ctx);
                if (!result.IsAllowed)
                {
                    _metrics.RecordRejected(gate.Layer);
                    return result;
                }
            }

            _metrics.RecordAllowed();
            return GateResult.Allow();
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
                _slotGate.Release(new ConnectionContext(ipAddress));
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

        // Internal (rather than private) so tests can drive channel-request handling directly
        // over a real SshChannel without needing the full TCP accept loop.
        internal void HandleShellChannelAsync(SshChannel channel, string ipAddress, CancellationToken sessionToken, CancellationTokenSource negotiationCts)
        {
            var channelCts = CancellationTokenSource.CreateLinkedTokenSource(sessionToken);
            channel.Closed += (_, _) => { try { channelCts.Cancel(); } catch { } };

            uint terminalWidth = 80;
            uint terminalHeight = 24;

            // Assigned once the "shell" request launches RunTuiInScopeAsync, and read from the
            // "window-change" handler which may run on a different thread (and may even fire
            // before "shell" - hence the null-conditional call below). A plain reference write is
            // atomic but not guaranteed visible across threads without a memory barrier, so both
            // sides go through Volatile rather than assuming ordering.
            ITuiEngine? engine = null;

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

                        // "window-change" carries cols/rows/pixel-width/pixel-height with no TERM
                        // string, unlike "pty-req" - TerminalRequestMessage.ConvertTo would
                        // misparse it (see WindowChangeMessage's remarks). Verified against the
                        // installed Microsoft.DevTunnels.Ssh 3.12.29 assembly: it has no dedicated
                        // window-change message type, so WindowChangeMessage mirrors the RFC 4254
                        // wire format directly.
                        var resizeMsg = e.Request.ConvertTo<WindowChangeMessage>();
                        if (resizeMsg != null)
                        {
                            Volatile.Read(ref engine)?.HandleResize(channel, resizeMsg.Columns, resizeMsg.Rows);
                        }
                        break;

                    case "shell":
                        e.IsAuthorized = true;

                        // DISARM: The shell is launching. Disable the 15-second countdown.
                        // Interactive keyboard idle timeout in TUI engine will now take over.
                        try { negotiationCts.CancelAfter(Timeout.InfiniteTimeSpan); } catch { }

                        if (!channelCts.IsCancellationRequested)  // <-- guard
                        {
                            _ = Task.Run(() => RunTuiInScopeAsync(
                                channel, ipAddress, terminalWidth, terminalHeight, channelCts.Token,
                                e2 => Volatile.Write(ref engine, e2)));
                        }
                        break;

                    // Benign channel requests OpenSSH clients send unprompted. None of these
                    // constitute an execution attempt, so accept-and-ignore rather than tearing
                    // down the session: "env" is the load-bearing one - OpenSSH's stock
                    // ssh_config on Ubuntu/Debian/macOS carries "SendEnv LANG LC_*", sent right
                    // after "pty-req" and before "shell", so refusing it kills the session before
                    // the TUI ever starts. We still discard the payload; we just don't apply it.
                    // "signal" is deliberately absent: the SSH library answers it before this
                    // handler runs and closes the session either way, verified by removing the
                    // case and observing identical behaviour. Listing it would claim a guarantee
                    // this switch cannot make.
                    case "env":
                    case "eow@openssh.com":
                    case "xon-xoff":
                    case "break":
                        e.IsAuthorized = true;
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

        /// <summary>
        /// Creates one DI scope, hands its <see cref="IServiceProvider"/> to <paramref name="work"/>,
        /// and tears the scope down when <paramref name="work"/> completes, throws, or is cancelled —
        /// "await using" guarantees disposal on all three. Internal (rather than private) so tests can
        /// drive this exact scoping seam directly instead of re-implementing the pattern.
        /// </summary>
        internal async Task RunInScopeAsync(Func<IServiceProvider, CancellationToken, Task> work, CancellationToken ct)
        {
            await using var scope = _serviceScopeFactory.CreateAsyncScope();
            await work(scope.ServiceProvider, ct);
        }

        /// <summary>
        /// Creates one DI scope per shell channel so every per-session service (TUI engine,
        /// command service, game state, etc.) gets its own instance instead of sharing the
        /// process-wide singleton graph.
        /// </summary>
        /// <param name="onEngineResolved">
        /// Invoked with the scope's <see cref="ITuiEngine"/> as soon as it is resolved, so the
        /// caller's "window-change" handler has something to reach even before RunAsync completes
        /// (and can keep reaching it, via a null-conditional call, if it fires before this runs).
        /// </param>
        private Task RunTuiInScopeAsync(
            SshChannel channel, string ipAddress, uint width, uint height, CancellationToken ct,
            Action<ITuiEngine> onEngineResolved) =>
            RunInScopeAsync(async (sp, token) =>
            {
                // Task 24C: register this session for the life of the scope, deregister in the
                // finally so a session that ends by exception doesn't linger in /who forever -
                // the same acquire/release shape as PerIpConcurrencyGate.Release. UserSessionState
                // is resolved only to read its SessionId (a plain string) so /who's own-row
                // highlight lines up with the id CommandContext already hands every command -
                // never the UserSessionState reference itself, which a singleton must not hold.
                // GetService (not Required): ChannelRequestSecurityTests builds a container with
                // only ITuiEngine registered, and that test never reads the registry back.
                var sessionId = sp.GetService<UserSessionState>()?.SessionId ?? Guid.NewGuid().ToString();
                _sessionRegistry.Register(sessionId, ipAddress);
                try
                {
                    var engine = sp.GetRequiredService<ITuiEngine>();
                    onEngineResolved(engine);
                    await engine.RunAsync(channel, width, height, token);
                }
                finally
                {
                    _sessionRegistry.Deregister(sessionId);
                }
            }, ct);

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

        private void ReleaseAndClose(TcpClient? client)
        {
            _connectionSemaphore.Release();
            if (client != null) try { client.Close(); } catch { }
        }
    }
}