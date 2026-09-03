using HIM.Gateway.Services.SSH.Gates;
using HIM.Gateway.Services.SSH.Interfaces;
using HIM.Gateway.Services.SSH.Interfaces.IGates;
using Microsoft.Extensions.Logging.Abstractions;

namespace HIM.Gateway.Tests;

/// <summary>L1 — IP ban check. No sockets; a fake IIpBanService drives the decision.</summary>
public class IpBanGateTests
{
    private sealed class FakeIpBanService : IIpBanService
    {
        private readonly HashSet<string> _banned;
        public FakeIpBanService(params string[] banned) => _banned = new HashSet<string>(banned);
        public bool IsBanned(string ipAddress) => _banned.Contains(ipAddress);
        public void RecordStrike(string ipAddress) { }
        public void Prune() { }
        public IReadOnlyList<BannedIpSnapshot> GetActiveBans() => Array.Empty<BannedIpSnapshot>();
    }

    [Fact]
    public void BannedIp_IsRejected_WithReasonBanned()
    {
        var gate = new IpBanGate(new FakeIpBanService("203.0.113.9"), NullLogger<IpBanGate>.Instance);

        var result = gate.Evaluate(new ConnectionContext("203.0.113.9"));

        Assert.False(result.IsAllowed);
        Assert.Equal("Banned", result.Reason);
    }

    [Fact]
    public void UnbannedIp_IsAllowed()
    {
        var gate = new IpBanGate(new FakeIpBanService("203.0.113.9"), NullLogger<IpBanGate>.Instance);

        var result = gate.Evaluate(new ConnectionContext("198.51.100.1"));

        Assert.True(result.IsAllowed);
    }
}
