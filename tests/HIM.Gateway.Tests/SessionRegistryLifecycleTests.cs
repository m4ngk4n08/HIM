using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using HIM.Gateway.Models;
using HIM.Gateway.Services.SSH;
using HIM.Gateway.Services.SSH.Interfaces;
using HIM.Gateway.Services.SSH.Interfaces.IGates;
using Microsoft.DevTunnels.Ssh;
using Microsoft.DevTunnels.Ssh.Algorithms;
using Microsoft.DevTunnels.Ssh.Events;
using Microsoft.DevTunnels.Ssh.Messages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace HIM.Gateway.Tests;

/// <summary>
/// Task 24C: drives the real accept-to-shell path (same harness shape as
/// ChannelRequestSecurityTests) rather than calling SessionRegistryService.Deregister directly,
/// to prove SshServerListener's own finally actually runs - including when the TUI engine
/// throws, which is the leak case the brief calls out by name: a session that ends by exception
/// must not linger in /who forever.
/// </summary>
public class SessionRegistryLifecycleTests
{
    private sealed class SignalingTuiEngine : ITuiEngine
    {
        private readonly bool _throwOnRun;
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public SignalingTuiEngine(bool throwOnRun) => _throwOnRun = throwOnRun;

        public Task RunAsync(SshChannel channel, uint width, uint height, CancellationToken ct)
        {
            Reached.TrySetResult();
            if (_throwOnRun) throw new InvalidOperationException("simulated TUI crash");
            return Task.Delay(Timeout.InfiniteTimeSpan, ct).ContinueWith(_ => { }, TaskScheduler.Default);
        }

        public void HandleResize(SshChannel channel, uint width, uint height) { }
    }

    private sealed class NoopConnectionSlotGate : IConnectionSlotGate
    {
        public string Layer => "noop";
        public GateResult Evaluate(ConnectionContext ctx) => GateResult.Allow();
        public void Release(ConnectionContext ctx) { }
    }

    private sealed class NoopHostKeyService : IHostKeyService
    {
        private readonly IKeyPair _key;
        public NoopHostKeyService(IKeyPair key) => _key = key;
        public Task<IKeyPair> GetHostKeyAsync() => Task.FromResult(_key);
    }

    private sealed class AllowAllAuthenticationService : IAuthenticationService
    {
        public void Authenticate(object? sender, SshAuthenticatingEventArgs e)
        {
            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.Name, e.Username ?? "explorer") }, "SSH"));
            e.AuthenticationTask = Task.FromResult<ClaimsPrincipal?>(principal);
        }
    }

    private sealed class Harness : IAsyncDisposable
    {
        public required SshServerListener Listener;
        public required SshServerSession ServerSession;
        public required SshClientSession ClientSession;
        public required SignalingTuiEngine TuiEngine;
        public required ISessionRegistryService SessionRegistry;
        public required TcpListener TcpListener;
        public required TcpClient ServerTcp;
        public required TcpClient ClientTcp;
        public required CancellationTokenSource NegotiationCts;

        public async ValueTask DisposeAsync()
        {
            try { await ClientSession.CloseAsync(SshDisconnectReason.ByApplication, "test complete"); } catch { }
            ServerSession.Dispose();
            ClientTcp.Dispose();
            ServerTcp.Dispose();
            TcpListener.Stop();
            NegotiationCts.Dispose();
        }
    }

    private static async Task<Harness> ConnectAsync(bool tuiThrows)
    {
        var services = new ServiceCollection();
        var tuiEngine = new SignalingTuiEngine(tuiThrows);
        services.AddSingleton<ITuiEngine>(tuiEngine);
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        var rsa = new Rsa("ssh-rsa", "SHA256");
        var hostKey = rsa.GenerateKeyPair(2048);
        var sessionRegistry = new SessionRegistryService(TimeProvider.System);

        var listener = new SshServerListener(
            scopeFactory,
            hostKeyService: new NoopHostKeyService(hostKey),
            authenticationService: new AllowAllAuthenticationService(),
            gates: Array.Empty<IConnectionGate>(),
            slotGate: new NoopConnectionSlotGate(),
            logger: NullLogger<SshServerListener>.Instance,
            settings: Options.Create(new SshSettings()),
            metrics: new ConnectionMetricsService(Array.Empty<IConnectionGate>(), TimeProvider.System),
            sessionRegistry: sessionRegistry);

        var tcpListener = new TcpListener(IPAddress.Loopback, 0);
        tcpListener.Start();
        var port = ((IPEndPoint)tcpListener.LocalEndpoint).Port;

        var acceptTask = tcpListener.AcceptTcpClientAsync();
        var clientTcp = new TcpClient();
        await clientTcp.ConnectAsync(IPAddress.Loopback, port);
        var serverTcp = await acceptTask;

        var config = SshSessionConfiguration.Default;
        var trace = new TraceSource("test", SourceLevels.Warning);

        var serverSession = new SshServerSession(config, trace)
        {
            Credentials = new[] { hostKey }
        };
        serverSession.Authenticating += (sender, e) =>
        {
            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.Name, e.Username ?? "explorer") }, "SSH"));
            e.AuthenticationTask = Task.FromResult<ClaimsPrincipal?>(principal);
        };

        var negotiationCts = new CancellationTokenSource();
        negotiationCts.CancelAfter(TimeSpan.FromSeconds(30));

        serverSession.ChannelOpening += (sender, e) =>
        {
            if (e.Channel.ChannelType == "session")
            {
                listener.HandleShellChannelAsync(e.Channel, "203.0.113.9", CancellationToken.None, negotiationCts);
                _ = Task.Run(() => e.Channel.Session.AcceptChannelAsync(CancellationToken.None));
            }
        };

        var clientSession = new SshClientSession(config, trace);
        clientSession.Authenticating += (sender, e) =>
        {
            e.AuthenticationTask = Task.FromResult<ClaimsPrincipal?>(new ClaimsPrincipal());
        };

        var serverConnect = serverSession.ConnectAsync(serverTcp.GetStream(), CancellationToken.None);
        var clientConnect = clientSession.ConnectAsync(clientTcp.GetStream(), CancellationToken.None);
        await Task.WhenAll(serverConnect, clientConnect);

        var authenticated = await clientSession.AuthenticateAsync(
            new SshClientCredentials("tester", "anypassword"), CancellationToken.None);
        Assert.True(authenticated);

        return new Harness
        {
            Listener = listener,
            ServerSession = serverSession,
            ClientSession = clientSession,
            TuiEngine = tuiEngine,
            SessionRegistry = sessionRegistry,
            TcpListener = tcpListener,
            ServerTcp = serverTcp,
            ClientTcp = clientTcp,
            NegotiationCts = negotiationCts
        };
    }

    private static async Task<SshChannel> OpenShellAsync(Harness h, CancellationToken ct)
    {
        var channel = await h.ClientSession.OpenChannelAsync(
            new ChannelOpenMessage { ChannelType = "session" }, null, ct);

        var ptyOk = await channel.RequestAsync(
            new TerminalRequestMessage { RequestType = "pty-req", WantReply = true, Term = "xterm", Columns = 80, Rows = 24 },
            ct);
        Assert.True(ptyOk);

        var shellOk = await channel.RequestAsync(
            new ShellRequestMessage { RequestType = "shell", WantReply = true }, ct);
        Assert.True(shellOk);

        return channel;
    }

    private static async Task WaitUntilEmptyAsync(ISessionRegistryService registry, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (registry.GetActiveSessions().Count == 0) return;
            await Task.Delay(20);
        }
        Assert.Empty(registry.GetActiveSessions());
    }

    [Fact]
    public async Task SessionThatEndsCleanly_IsDeregistered()
    {
        await using var h = await ConnectAsync(tuiThrows: false);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await OpenShellAsync(h, cts.Token);
        var reached = await Task.WhenAny(h.TuiEngine.Reached.Task, Task.Delay(TimeSpan.FromSeconds(10), cts.Token));
        Assert.Same(h.TuiEngine.Reached.Task, reached);

        Assert.NotEmpty(h.SessionRegistry.GetActiveSessions());

        // Ends the session cleanly by closing the client - mirrors a visitor typing /exit or
        // disconnecting normally.
        await h.ClientSession.CloseAsync(SshDisconnectReason.ByApplication, "test complete");

        await WaitUntilEmptyAsync(h.SessionRegistry, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task SessionThatEndsByException_IsStillDeregistered()
    {
        // The leak case: if Deregister isn't in a finally, a session whose TUI engine throws
        // stays in the registry forever and /who slowly fills with ghosts.
        await using var h = await ConnectAsync(tuiThrows: true);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await OpenShellAsync(h, cts.Token);
        var reached = await Task.WhenAny(h.TuiEngine.Reached.Task, Task.Delay(TimeSpan.FromSeconds(10), cts.Token));
        Assert.Same(h.TuiEngine.Reached.Task, reached);

        await WaitUntilEmptyAsync(h.SessionRegistry, TimeSpan.FromSeconds(5));
    }
}
