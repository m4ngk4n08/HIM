using HIM.Gateway.Models;
using HIM.Gateway.Services.SSH.Interfaces;
using HIM.Gateway.Services.SSH.Interfaces.IGates;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace HIM.Gateway.Services.SSH.Gates
{
    /// <summary>
    /// Layer 5 — per-IP concurrent connection cap. Acquires a slot during Evaluate; the matching
    /// release happens once the connection this gate admitted has ended, via Release(), which
    /// the listener calls from its own finally block (resolved as IConnectionSlotGate).
    /// </summary>
    public sealed class PerIpConcurrencyGate : IConnectionSlotGate
    {
        private readonly SshSettings _settings;
        private readonly IIpBanService _ipBanService;
        private readonly ILogger<PerIpConcurrencyGate> _logger;
        private readonly ConcurrentDictionary<string, int> _activeConnectionsPerIp = new();

        public string Layer => "L5 PerIpConcurrency";

        public PerIpConcurrencyGate(
            IOptions<SshSettings> settings,
            IIpBanService ipBanService,
            ILogger<PerIpConcurrencyGate> logger)
        {
            _settings = settings.Value;
            _ipBanService = ipBanService;
            _logger = logger;
        }

        public GateResult Evaluate(ConnectionContext ctx)
        {
            int active = _activeConnectionsPerIp.AddOrUpdate(ctx.IpAddress, 1, (_, val) => val + 1);
            if (active > _settings.MaxConcurrentPerIp)
            {
                Decrement(ctx.IpAddress);
                _ipBanService.RecordStrike(ctx.IpAddress);
                _logger.LogWarning(
                    "[Security] {Timestamp:yyyy-MM-dd HH:mm:ss} UTC | CONCURRENT LIMIT | " +
                    "IP: {IpAddress} | Active: {Active}/{Max}",
                    DateTime.UtcNow, ctx.IpAddress, active, _settings.MaxConcurrentPerIp);
                return GateResult.Reject("RateOrConcurrentLimit");
            }

            return GateResult.Allow();
        }

        public void Release(ConnectionContext ctx) => Decrement(ctx.IpAddress);

        private void Decrement(string ipAddress)
        {
            if (string.IsNullOrEmpty(ipAddress)) return;
            _activeConnectionsPerIp.AddOrUpdate(ipAddress, 0, (_, val) => Math.Max(0, val - 1));
        }
    }
}
