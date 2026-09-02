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

namespace HIM.Gateway.Tests;

/// <summary>
/// Regression coverage for SshServerListener.HandleShellChannelAsync's channel-request switch
/// (Task 11): "env" - and the other benign requests OpenSSH clients send unprompted - must be
/// accepted and ignored rather than tearing down the session, while actual execution attempts
/// ("exec", "subsystem") must still be refused and still disconnect. Drives the real
/// Microsoft.DevTunnels.Ssh client/server stack over a loopback TCP socket so the assertions
/// exercise the wire protocol, not a mocked event args object.
/// </summary>
public class ChannelRequestSecurityTests
{
    private sealed class SignalingTuiEngine : ITuiEngine
    {
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task RunAsync(SshChannel channel, uint width, uint height, CancellationToken ct)
        {
            Reached.TrySetResult();
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

    private sealed class Harness : IAsyncDisposable
    {
        public required SshServerListener Listener;
        public required SshServerSession ServerSession;
        public required SshClientSession ClientSession;
        public required SignalingTuiEngine TuiEngine;
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

    private static async Task<Harness> ConnectAsync()
    {
        var services = new ServiceCollection();
        var tuiEngine = new SignalingTuiEngine();
        services.AddSingleton<ITuiEngine>(tuiEngine);
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        var rsa = new Rsa("ssh-rsa", "SHA256");
        var hostKey = rsa.GenerateKeyPair(2048);

        var listener = new SshServerListener(
            scopeFactory,
            hostKeyService: new StaticHostKeyService(hostKey),
            authenticationService: new AllowAllAuthenticationService(),
            gates: Array.Empty<IConnectionGate>(),
            slotGate: new NoopConnectionSlotGate(),
            logger: NullLogger<SshServerListener>.Instance,
            settings: Options.Create(new SshSettings()));

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
                // Subscribe to the channel's Request event synchronously, before the
                // channel-open confirmation is sent (AcceptChannelAsync below), so there is
                // no window where a fast client's first channel request arrives unhandled.
                listener.HandleShellChannelAsync(e.Channel, "127.0.0.1", CancellationToken.None, negotiationCts);
                _ = Task.Run(() => e.Channel.Session.AcceptChannelAsync(CancellationToken.None));
            }
        };

        var clientSession = new SshClientSession(config, trace);
        clientSession.Authenticating += (sender, e) =>
        {
            // Test-only trust-on-first-use: accept whatever host key the server presents.
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
            TcpListener = tcpListener,
            ServerTcp = serverTcp,
            ClientTcp = clientTcp,
            NegotiationCts = negotiationCts
        };
    }

    private sealed class StaticHostKeyService : IHostKeyService
    {
        private readonly IKeyPair _key;
        public StaticHostKeyService(IKeyPair key) => _key = key;
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

    // Every request type the fix accepts, not just "env" - otherwise deleting one of the other
    // three cases breaks no test. "eow@openssh.com" is the next most likely to matter: OpenSSH
    // clients send it at normal channel EOF, so refusing it would tear down a live session.
    [Theory]
    [InlineData("env")]
    [InlineData("eow@openssh.com")]
    [InlineData("xon-xoff")]
    [InlineData("break")]
    public async Task BenignRequest_IsAcceptedAndIgnored_SessionSurvivesToShell(string requestType)
    {
        await using var h = await ConnectAsync();
        var timeout = TimeSpan.FromSeconds(10);
        using var cts = new CancellationTokenSource(timeout);

        var channel = await h.ClientSession.OpenChannelAsync(
            new ChannelOpenMessage { ChannelType = "session" }, null, cts.Token);

        var ptyOk = await channel.RequestAsync(
            new TerminalRequestMessage { RequestType = "pty-req", WantReply = true, Term = "xterm", Columns = 80, Rows = 24 },
            cts.Token);
        Assert.True(ptyOk);

        var benignOk = await channel.RequestAsync(
            new ChannelRequestMessage { RequestType = requestType, WantReply = true },
            cts.Token);
        Assert.True(benignOk);

        Assert.False(h.ServerSession.IsClosed);
        Assert.False(channel.IsClosed);

        var shellOk = await channel.RequestAsync(
            new ShellRequestMessage { RequestType = "shell", WantReply = true },
            cts.Token);
        Assert.True(shellOk);

        var reachedTui = await Task.WhenAny(h.TuiEngine.Reached.Task, Task.Delay(timeout, cts.Token));
        Assert.Same(h.TuiEngine.Reached.Task, reachedTui);

        Assert.False(h.ServerSession.IsClosed);
    }

    [Theory]
    [InlineData("exec")]
    [InlineData("subsystem")]
    public async Task ExecutionRequest_IsRefusedAndDisconnects(string requestType)
    {
        await using var h = await ConnectAsync();
        var timeout = TimeSpan.FromSeconds(10);
        using var cts = new CancellationTokenSource(timeout);

        var channel = await h.ClientSession.OpenChannelAsync(
            new ChannelOpenMessage { ChannelType = "session" }, null, cts.Token);

        var ptyOk = await channel.RequestAsync(
            new TerminalRequestMessage { RequestType = "pty-req", WantReply = true, Term = "xterm", Columns = 80, Rows = 24 },
            cts.Token);
        Assert.True(ptyOk);

        var refused = await channel.RequestAsync(
            new ChannelRequestMessage { RequestType = requestType, WantReply = true },
            cts.Token);
        Assert.False(refused);

        var closedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.ServerSession.Closed += (_, _) => closedTcs.TrySetResult();
        if (h.ServerSession.IsClosed) closedTcs.TrySetResult();

        var closed = await Task.WhenAny(closedTcs.Task, Task.Delay(timeout, cts.Token));
        Assert.Same(closedTcs.Task, closed);
    }
}
