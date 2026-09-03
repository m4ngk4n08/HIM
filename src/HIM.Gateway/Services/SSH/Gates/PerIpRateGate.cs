using HIM.Gateway.Models;
using HIM.Gateway.Services.SSH.Interfaces;
using HIM.Gateway.Services.SSH.Interfaces.IGates;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace HIM.Gateway.Services.SSH.Gates
{
    /// <summary>
    /// Layer 4 — per-IP sliding-window rate limit. Owns _connectionHistory and its pruning; no
    /// other class touches this dictionary.
    ///
    /// Evaluation-order contract with PerIpConcurrencyGate (L5): this gate's Enqueue happens
    /// during THIS gate's own Evaluate, before L5 runs (registration order is evaluation order —
    /// see ServiceExtensions.AddConnectionGate). So an attempt this gate allows but L5 later
    /// rejects still counts against this window. Do not "tidy" this by moving the enqueue after
    /// some later check — that silently weakens the rate limit. A rejected attempt here does
    /// NOT enqueue, so it does not extend its own window.
    ///
    /// Pruning trigger note: the pre-extraction code triggered PruneConnectionHistory from an
    /// accept-counter in SshServerListener, incremented only after every gate passed. This gate
    /// self-triggers instead, off its own evaluation count (every PruneHistoryEvery calls to
    /// Evaluate, regardless of this gate's own outcome) — deliberately, so the dictionary and its
    /// pruning stay owned by one class per the 16D brief. Slightly more frequent sweeps under
    /// load than before; harmless, since pruning is idempotent maintenance.
    /// </summary>
    public sealed class PerIpRateGate : IConnectionGate
    {
        private const int PruneHistoryEvery = 500;

        private readonly SshSettings _settings;
        private readonly IIpBanService _ipBanService;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger<PerIpRateGate> _logger;
        private readonly ConcurrentDictionary<string, ConcurrentQueue<DateTime>> _connectionHistory = new();
        private int _evaluationCounter;

        public string Layer => "L4 PerIpRate";

        /// <summary>Test/panel-only window into _connectionHistory's size, same shape as
        /// PerIpConcurrencyGate.TrackedIpCount.</summary>
        internal int TrackedIpCount => _connectionHistory.Count;

        public PerIpRateGate(
            IOptions<SshSettings> settings,
            IIpBanService ipBanService,
            TimeProvider timeProvider,
            ILogger<PerIpRateGate> logger)
        {
            _settings = settings.Value;
            _ipBanService = ipBanService;
            _timeProvider = timeProvider;
            _logger = logger;
        }

        public GateResult Evaluate(ConnectionContext ctx)
        {
            var result = EvaluateCore(ctx);

            if (Interlocked.Increment(ref _evaluationCounter) % PruneHistoryEvery == 0)
                _ = Task.Run(PruneHistory);

            return result;
        }

        private GateResult EvaluateCore(ConnectionContext ctx)
        {
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var history = _connectionHistory.GetOrAdd(ctx.IpAddress, _ => new ConcurrentQueue<DateTime>());

            // Prune old entries out of the queue snapshot asynchronously
            var cutoff = now.AddSeconds(-_settings.RateLimitWindowSeconds);
            while (history.TryPeek(out var oldest) && oldest < cutoff)
            {
                history.TryDequeue(out _);
            }

            if (history.Count >= _settings.RateLimitMaxAttempts)
            {
                _ipBanService.RecordStrike(ctx.IpAddress);
                _logger.LogWarning(
                    "[Security] {Timestamp:yyyy-MM-dd HH:mm:ss} UTC | RATE LIMIT | " +
                    "IP: {IpAddress} | Attempts: {Count}/{Max} in {Window}s",
                    DateTime.UtcNow, ctx.IpAddress,
                    history.Count, _settings.RateLimitMaxAttempts, _settings.RateLimitWindowSeconds);
                return GateResult.Reject("RateOrConcurrentLimit");
            }

            history.Enqueue(now);
            return GateResult.Allow();
        }

        /// <summary>
        /// Removes IPs from the sliding-window history that have had no activity within the
        /// rate-limit window. Runs on the thread pool every PruneHistoryEvery evaluations.
        /// Memory safety guarantee: dictionary size is bounded by the number of unique IPs seen
        /// within any given window, not cumulative lifetime.
        /// </summary>
        internal void PruneHistory()
        {
            var cutoff = _timeProvider.GetUtcNow().UtcDateTime.AddSeconds(-_settings.RateLimitWindowSeconds);
            var toRemove = new List<string>();

            foreach (var (key, history) in _connectionHistory)
            {
                while (history.TryPeek(out var oldest) && oldest < cutoff)
                {
                    history.TryDequeue(out _);
                }
                if (history.IsEmpty) toRemove.Add(key);
            }

            foreach (var key in toRemove)
                _connectionHistory.TryRemove(key, out _);

            _logger.LogDebug(
                "[Gateway] {Timestamp:yyyy-MM-dd HH:mm:ss} UTC | History pruned | " +
                "Removed: {Removed} IPs | Tracked: {Tracked} IPs",
                DateTime.UtcNow, toRemove.Count, _connectionHistory.Count);
        }
    }
}
