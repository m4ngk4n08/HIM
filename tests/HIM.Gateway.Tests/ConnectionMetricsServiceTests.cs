using HIM.Gateway.Services.SSH;
using HIM.Gateway.Services.SSH.Interfaces.IGates;
using Microsoft.Extensions.Time.Testing;

namespace HIM.Gateway.Tests;

/// <summary>
/// Task 23C: per-layer accept/reject counters, filled in from the same layer names
/// ConnectionGatePipelineTests already pins - no gate class involved here, just the metrics
/// service and a fake TimeProvider.
/// </summary>
public class ConnectionMetricsServiceTests
{
    private sealed class FakeGate : IConnectionGate
    {
        public FakeGate(string layer) => Layer = layer;
        public string Layer { get; }
        public GateResult Evaluate(ConnectionContext ctx) => throw new NotSupportedException();
    }

    private static ConnectionMetricsService CreateService(FakeTimeProvider timeProvider) =>
        new(new IConnectionGate[]
        {
            new FakeGate("L3 GlobalFlood"),
            new FakeGate("L1 IpBan"),
            new FakeGate("L4 PerIpRate"),
            new FakeGate("L5 PerIpConcurrency")
        }, timeProvider);

    [Fact]
    public void RecordRejected_ForOneLayer_IncrementsOnlyThatLayersCounter()
    {
        var service = CreateService(new FakeTimeProvider());

        service.RecordRejected("L4 PerIpRate");

        var snapshot = service.GetSnapshot();
        Assert.Equal(1, snapshot.TotalRejected);
        Assert.Equal(0, snapshot.TotalAllowed);
        Assert.Equal(1, snapshot.TotalEvaluated);

        foreach (var (layer, rejected) in snapshot.RejectionsPerLayer)
            Assert.Equal(layer == "L4 PerIpRate" ? 1 : 0, rejected);
    }

    [Fact]
    public void RecordAllowed_IncrementsAllowed_NotRejected()
    {
        var service = CreateService(new FakeTimeProvider());

        service.RecordAllowed();

        var snapshot = service.GetSnapshot();
        Assert.Equal(1, snapshot.TotalAllowed);
        Assert.Equal(0, snapshot.TotalRejected);
        Assert.Equal(1, snapshot.TotalEvaluated);
        Assert.All(snapshot.RejectionsPerLayer, r => Assert.Equal(0, r.Rejected));
    }

    [Fact]
    public void Uptime_IsDrivenByTimeProvider_NotWallClock()
    {
        var timeProvider = new FakeTimeProvider();
        var service = CreateService(timeProvider);

        timeProvider.Advance(TimeSpan.FromMinutes(90));

        var snapshot = service.GetSnapshot();
        Assert.Equal(TimeSpan.FromMinutes(90), snapshot.Uptime);
    }
}
