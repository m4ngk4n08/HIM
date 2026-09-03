using HIM.Gateway.Extensions;
using HIM.Gateway.Models;
using HIM.Gateway.Services.SSH;
using HIM.Gateway.Services.SSH.Interfaces;
using HIM.Gateway.Services.SSH.Interfaces.IGates;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Linq;

namespace HIM.Gateway.Tests;

/// <summary>
/// Pipeline-level coverage the individual gate tests can't provide: that DI registration order
/// really is evaluation order, and that the accept loop's short-circuit (first rejecting gate
/// wins, later gates never run) is explicit rather than an if-ladder accident.
/// </summary>
public class ConnectionGatePipelineTests
{
    [Fact]
    public void RegistrationOrder_IsEvaluationOrder_L3ThenL1ThenL4ThenL5()
    {
        // The real container, built the same way Program.cs does - proves
        // IEnumerable<IConnectionGate> resolves in the order AddService() registers gates,
        // not just that the four gate types exist. This is the test that keeps the README's
        // ordered diagram true.
        using var provider = GatewayServiceProviderFactory.Build();

        var layers = provider.GetServices<IConnectionGate>().Select(g => g.Layer).ToArray();

        Assert.Equal(
            new[] { "L3 GlobalFlood", "L1 IpBan", "L4 PerIpRate", "L5 PerIpConcurrency" },
            layers);
    }

    private sealed class RecordingGate : IConnectionGate
    {
        private readonly GateResult _result;
        public string Layer { get; }
        public bool WasEvaluated { get; private set; }

        public RecordingGate(string layer, GateResult result)
        {
            Layer = layer;
            _result = result;
        }

        public GateResult Evaluate(ConnectionContext ctx)
        {
            WasEvaluated = true;
            return _result;
        }
    }

    private sealed class NoopHostKeyService : IHostKeyService
    {
        public Task<Microsoft.DevTunnels.Ssh.Algorithms.IKeyPair> GetHostKeyAsync() =>
            Task.FromResult<Microsoft.DevTunnels.Ssh.Algorithms.IKeyPair>(null!);
    }

    private sealed class NoopAuthenticationService : IAuthenticationService
    {
        public void Authenticate(object? sender, Microsoft.DevTunnels.Ssh.Events.SshAuthenticatingEventArgs e) { }
    }

    private sealed class NoopConnectionSlotGate : IConnectionSlotGate
    {
        public string Layer => "noop-slot";
        public GateResult Evaluate(ConnectionContext ctx) => GateResult.Allow();
        public void Release(ConnectionContext ctx) { }
    }

    [Fact]
    public void FirstRejectingGate_Wins_AndLaterGatesDoNotRun()
    {
        // Mirrors a scanner IP that is both banned (L1) and over the rate limit (L4): today's
        // if-ladder short-circuits on the first failing check. Drives SshServerListener.
        // EvaluateGates directly - the real method the accept loop calls - rather than a
        // re-implementation of its loop, so a regression in the actual short-circuit is caught.
        var allow = new RecordingGate("L3 GlobalFlood", GateResult.Allow());
        var banned = new RecordingGate("L1 IpBan", GateResult.Reject("Banned"));
        var rateLimited = new RecordingGate("L4 PerIpRate", GateResult.Reject("RateOrConcurrentLimit"));

        var scopeFactory = new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        var gates = new IConnectionGate[] { allow, banned, rateLimited };
        var listener = new SshServerListener(
            scopeFactory,
            new NoopHostKeyService(),
            new NoopAuthenticationService(),
            gates: gates,
            slotGate: new NoopConnectionSlotGate(),
            logger: NullLoggerFactory.Instance.CreateLogger<SshServerListener>(),
            settings: Options.Create(new SshSettings()),
            metrics: new ConnectionMetricsService(gates, TimeProvider.System),
            sessionRegistry: new SessionRegistryService(TimeProvider.System));

        var result = listener.EvaluateGates(new ConnectionContext("203.0.113.9"));

        Assert.False(result.IsAllowed);
        Assert.Equal("Banned", result.Reason);
        Assert.True(allow.WasEvaluated);
        Assert.True(banned.WasEvaluated);
        Assert.False(rateLimited.WasEvaluated);
    }
}
