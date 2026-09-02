using HIM.Gateway.Models;
using HIM.Gateway.Services.SSH.Gates;
using HIM.Gateway.Services.SSH.Interfaces.IGates;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace HIM.Gateway.Tests;

/// <summary>
/// L3 — global flood guard. Deterministic via FakeTimeProvider; no Task.Delay/Thread.Sleep.
/// </summary>
public class GlobalFloodGateTests
{
    private static GlobalFloodGate CreateGate(FakeTimeProvider time, int maxPerSecond = 5)
    {
        var settings = Options.Create(new SshSettings { MaxGlobalConnectionsPerSecond = maxPerSecond });
        return new GlobalFloodGate(settings, time, NullLogger<GlobalFloodGate>.Instance);
    }

    [Fact]
    public void AdmitsExactlyTheConfiguredLimit_ThenRejectsTheNext()
    {
        var time = new FakeTimeProvider();
        var gate = CreateGate(time, maxPerSecond: 5);
        var ctx = new ConnectionContext("203.0.113.1");

        for (var i = 0; i < 5; i++)
            Assert.True(gate.Evaluate(ctx).IsAllowed);

        var sixth = gate.Evaluate(ctx);
        Assert.False(sixth.IsAllowed);
        Assert.Equal("GlobalFloodLimit", sixth.Reason);
    }

    [Fact]
    public void AdmitsAgain_AfterTheWindowAdvancesPastOneSecond()
    {
        var time = new FakeTimeProvider();
        var gate = CreateGate(time, maxPerSecond: 2);
        var ctx = new ConnectionContext("203.0.113.1");

        Assert.True(gate.Evaluate(ctx).IsAllowed);
        Assert.True(gate.Evaluate(ctx).IsAllowed);
        Assert.False(gate.Evaluate(ctx).IsAllowed);

        time.Advance(TimeSpan.FromSeconds(1));

        Assert.True(gate.Evaluate(ctx).IsAllowed);
    }

    [Fact]
    public void TheWindowResetWinner_AlwaysGetsASlot_EvenWhenTheLimitIsZero()
    {
        var time = new FakeTimeProvider();
        var gate = CreateGate(time, maxPerSecond: 0);
        var ctx = new ConnectionContext("203.0.113.1");

        // Advance past the 1-second window before the first call, so this call is the
        // window-reset winner - it must get a slot even though the limit is 0.
        time.Advance(TimeSpan.FromSeconds(2));
        Assert.True(gate.Evaluate(ctx).IsAllowed);

        // Same (now-current) window, limit already exhausted by the reset winner.
        Assert.False(gate.Evaluate(ctx).IsAllowed);
    }
}
