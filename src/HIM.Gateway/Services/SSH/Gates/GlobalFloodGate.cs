using HIM.Gateway.Models;
using HIM.Gateway.Services.SSH.Interfaces.IGates;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HIM.Gateway.Services.SSH.Gates
{
    /// <summary>
    /// Layer 3 — global flood guard. Lock-free sliding per-second token bucket.
    /// Uses CAS on _floodWindowStart to elect a single "window-reset winner" among concurrent
    /// threads. All losers proceed to the Interlocked.Increment path, which is also lock-free.
    /// Worst-case: MaxGlobalConnectionsPerSecond+1 connections admitted in a window boundary
    /// race — acceptable overage for a coarse global guard.
    /// </summary>
    public sealed class GlobalFloodGate : IConnectionGate
    {
        private readonly SshSettings _settings;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger<GlobalFloodGate> _logger;

        private long _floodWindowStart;
        private int _floodWindowCount;

        public string Layer => "L3 GlobalFlood";

        public GlobalFloodGate(IOptions<SshSettings> settings, TimeProvider timeProvider, ILogger<GlobalFloodGate> logger)
        {
            _settings = settings.Value;
            _timeProvider = timeProvider;
            _logger = logger;
            _floodWindowStart = timeProvider.GetTimestamp();
        }

        public GateResult Evaluate(ConnectionContext ctx)
        {
            if (TryConsumeGlobalSlot())
                return GateResult.Allow();

            _logger.LogWarning(
                "[Security] {Timestamp:yyyy-MM-dd HH:mm:ss} UTC | GLOBAL FLOOD LIMIT | " +
                "Rejected: {IpAddress} | Limit: {Limit}/sec",
                DateTime.UtcNow, ctx.IpAddress, _settings.MaxGlobalConnectionsPerSecond);
            return GateResult.Reject("GlobalFloodLimit");
        }

        private bool TryConsumeGlobalSlot()
        {
            var now = _timeProvider.GetTimestamp();
            var windowStart = Interlocked.Read(ref _floodWindowStart);

            if (_timeProvider.GetElapsedTime(windowStart, now) >= TimeSpan.FromSeconds(1)) // 1-second window expired
            {
                // CAS: only one thread resets the window; others fall through.
                if (Interlocked.CompareExchange(ref _floodWindowStart, now, windowStart) == windowStart)
                {
                    Interlocked.Exchange(ref _floodWindowCount, 1);
                    return true; // window-reset thread always gets a slot
                }
            }

            return Interlocked.Increment(ref _floodWindowCount) <= _settings.MaxGlobalConnectionsPerSecond;
        }
    }
}
