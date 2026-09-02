using HIM.Gateway.Models;
using HIM.Gateway.Services.SSH.Gates;
using HIM.Gateway.Services.SSH.Interfaces;
using HIM.Gateway.Services.SSH.Interfaces.IGates;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HIM.Gateway.Tests;

/// <summary>L5 — per-IP concurrent connection cap. No sockets.</summary>
public class PerIpConcurrencyGateTests
{
    private sealed class FakeIpBanService : IIpBanService
    {
        public int StrikeCount;
        public bool IsBanned(string ipAddress) => false;
        public void RecordStrike(string ipAddress) => StrikeCount++;
        public void Prune() { }
    }

    private static (PerIpConcurrencyGate Gate, FakeIpBanService BanService) CreateGate(int maxConcurrent = 3)
    {
        var settings = Options.Create(new SshSettings { MaxConcurrentPerIp = maxConcurrent });
        var banService = new FakeIpBanService();
        var gate = new PerIpConcurrencyGate(settings, banService, NullLogger<PerIpConcurrencyGate>.Instance);
        return (gate, banService);
    }

    [Fact]
    public void AllowsUpToMaxConcurrent_ThenRejectsWithSharedReason()
    {
        var (gate, _) = CreateGate(maxConcurrent: 2);
        var ctx = new ConnectionContext("203.0.113.7");

        Assert.True(gate.Evaluate(ctx).IsAllowed);
        Assert.True(gate.Evaluate(ctx).IsAllowed);

        var third = gate.Evaluate(ctx);
        Assert.False(third.IsAllowed);
        Assert.Equal("RateOrConcurrentLimit", third.Reason);
    }

    [Fact]
    public void RejectionDecrementsItsOwnIncrement_SoTheCountDoesNotDrift()
    {
        var (gate, _) = CreateGate(maxConcurrent: 1);
        var ctx = new ConnectionContext("203.0.113.7");

        Assert.True(gate.Evaluate(ctx).IsAllowed);   // active = 1
        Assert.False(gate.Evaluate(ctx).IsAllowed);  // active would be 2, self-undoes back to 1
        Assert.False(gate.Evaluate(ctx).IsAllowed);  // if the undo didn't happen, active would keep growing

        gate.Release(ctx); // release the one legitimately-held slot (active -> 0)
        Assert.True(gate.Evaluate(ctx).IsAllowed);   // proves active was 0, not still inflated
    }

    [Fact]
    public void RejectionRecordsAStrike_OnTheSharedIpBanService()
    {
        var (gate, banService) = CreateGate(maxConcurrent: 1);
        var ctx = new ConnectionContext("203.0.113.7");

        gate.Evaluate(ctx);
        Assert.Equal(0, banService.StrikeCount);

        gate.Evaluate(ctx);
        Assert.Equal(1, banService.StrikeCount);
    }

    [Fact]
    public void Release_RestoresASlot()
    {
        var (gate, _) = CreateGate(maxConcurrent: 1);
        var ctx = new ConnectionContext("203.0.113.7");

        Assert.True(gate.Evaluate(ctx).IsAllowed);
        Assert.False(gate.Evaluate(ctx).IsAllowed);

        gate.Release(ctx);

        Assert.True(gate.Evaluate(ctx).IsAllowed);
    }
}
