using HIM.Gateway.Models;
using HIM.Gateway.Services.SSH.Gates;
using HIM.Gateway.Services.SSH.Interfaces;
using HIM.Gateway.Services.SSH.Interfaces.IGates;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace HIM.Gateway.Tests;

/// <summary>
/// L4 — per-IP sliding-window rate limit. Deterministic via FakeTimeProvider; no
/// Task.Delay/Thread.Sleep.
/// </summary>
public class PerIpRateGateTests
{
    private sealed class FakeIpBanService : IIpBanService
    {
        public int StrikeCount;
        public bool IsBanned(string ipAddress) => false;
        public void RecordStrike(string ipAddress) => StrikeCount++;
        public void Prune() { }
        public IReadOnlyList<BannedIpSnapshot> GetActiveBans() => Array.Empty<BannedIpSnapshot>();
    }

    private static (PerIpRateGate Gate, FakeIpBanService BanService) CreateGate(
        FakeTimeProvider time, int windowSeconds = 60, int maxAttempts = 3)
    {
        var settings = Options.Create(new SshSettings
        {
            RateLimitWindowSeconds = windowSeconds,
            RateLimitMaxAttempts = maxAttempts
        });
        var banService = new FakeIpBanService();
        var gate = new PerIpRateGate(settings, banService, time, NullLogger<PerIpRateGate>.Instance);
        return (gate, banService);
    }

    [Fact]
    public void AllowsUpToMaxAttempts_ThenRejectsWithSharedReason()
    {
        var time = new FakeTimeProvider();
        var (gate, _) = CreateGate(time, maxAttempts: 3);
        var ctx = new ConnectionContext("203.0.113.5");

        Assert.True(gate.Evaluate(ctx).IsAllowed);
        Assert.True(gate.Evaluate(ctx).IsAllowed);
        Assert.True(gate.Evaluate(ctx).IsAllowed);

        var fourth = gate.Evaluate(ctx);
        Assert.False(fourth.IsAllowed);
        Assert.Equal("RateOrConcurrentLimit", fourth.Reason);
    }

    [Fact]
    public void ARejectedAttempt_DoesNotEnqueue_SoTheIpIsAllowedAgainExactlyAtWindowExpiry()
    {
        var time = new FakeTimeProvider();
        var (gate, _) = CreateGate(time, windowSeconds: 10, maxAttempts: 1);
        var ctx = new ConnectionContext("203.0.113.5");

        Assert.True(gate.Evaluate(ctx).IsAllowed);   // consumes the only slot, enqueued at t=0
        Assert.False(gate.Evaluate(ctx).IsAllowed);  // rejected - must NOT enqueue (would extend the window)

        // Just before the single enqueued attempt (t=0) ages out of the 10s window: still blocked.
        time.Advance(TimeSpan.FromSeconds(9));
        Assert.False(gate.Evaluate(ctx).IsAllowed);

        // Once the window has fully elapsed the t=0 entry is pruned - allowed again. If the
        // rejected attempt above had wrongly enqueued (extending its own window), this would
        // still be blocked at this point.
        time.Advance(TimeSpan.FromSeconds(1) + TimeSpan.FromTicks(1));
        Assert.True(gate.Evaluate(ctx).IsAllowed);
    }

    [Fact]
    public void RejectionRecordsAStrike_OnTheSharedIpBanService()
    {
        var time = new FakeTimeProvider();
        var (gate, banService) = CreateGate(time, maxAttempts: 1);
        var ctx = new ConnectionContext("203.0.113.5");

        gate.Evaluate(ctx);
        Assert.Equal(0, banService.StrikeCount);

        gate.Evaluate(ctx);
        Assert.Equal(1, banService.StrikeCount);
    }
}
