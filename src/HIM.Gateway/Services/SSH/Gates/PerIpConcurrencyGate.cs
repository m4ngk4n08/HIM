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

        /// <summary>Test-only window into _activeConnectionsPerIp's size — proves the leak fix.</summary>
        internal int TrackedIpCount => _activeConnectionsPerIp.Count;

        /// <summary>
        /// Decrements the IP's active count, removing the entry entirely once it reaches zero so
        /// a public port under continuous scanning doesn't accumulate one permanent zero-valued
        /// entry per unique IP for the life of the process. Uses TryRemove(KeyValuePair) rather
        /// than a plain TryRemove(key) so a concurrent Evaluate racing this decrement (incrementing
        /// the same entry between our read and the remove) cannot have its increment silently
        /// dropped - if the observed value has changed by the time we act, we retry instead of
        /// removing or overwriting a value we no longer know is stale.
        /// </summary>
        private void Decrement(string ipAddress)
        {
            if (string.IsNullOrEmpty(ipAddress)) return;

            while (_activeConnectionsPerIp.TryGetValue(ipAddress, out var current))
            {
                var next = current - 1;
                if (next <= 0)
                {
                    if (_activeConnectionsPerIp.TryRemove(new KeyValuePair<string, int>(ipAddress, current)))
                        return;
                }
                else if (_activeConnectionsPerIp.TryUpdate(ipAddress, next, current))
                {
                    return;
                }
                // Another thread changed the entry between our read and our write - retry with
                // the now-current value rather than clobbering a concurrent increment/decrement.
            }
        }
    }
}
