using HIM.Gateway.Models;
using HIM.Gateway.Services.SSH.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;

namespace HIM.Gateway.Services.SSH
{
    /// <summary>
    /// Thred-safe IP Ban service.
    /// 
    /// Design decision:
    /// - ConcurrentDictionary + record struct: lock-free reads on the hot path(isBanned)
    /// The only lock is AddOrUpdate's internal per-bucket lock
    /// which is extremely fine-grained.
    /// 
    /// - BanEntry is a readonly record struct(value type, immutable):
    /// copied into the dictionary atomically by reference on 64-bit .NET-
    /// no heap allocation on update.
    /// 
    /// - DatTime.UtcNow.Ticks for ban expiry: avoids Environment.TickCount64
    /// wrap-around edge cases (TickCount64 is milliseconds from boot; DatTime.Ticks never overflows in practice).
    /// Conversion to DatTime for display is trivial.
    /// 
    /// - Exponential ban escalation: 10min -> 1hr -> 24hr -> 6days.
    /// Persistent bots are permanently slowed without requiring external block list.
    /// 
    /// - Periodic Prune via Interlocked counter: no background Timer/Thread.
    /// Prune is triggered every PruneEveryStrikes operations and runs on
    /// the ThreadPool - zero impact on the accept loop.
    /// 
    /// - Memory safety: Prune removes entries that are either:
    /// (a) expired > 1 hour ago and not a "repeat offender" (generation 0), or
    /// (b) expired > BanDuration*2 ago regardless of generation.
    /// This prevents the dictionary from growing unboundedly under sustained
    /// DDos while retaining escalation history for repeat offenders.
    /// </summary>
    public sealed class IpBanService : IIpBanService
    {
        // Value-typed entry stored per IP. Immutable - updated via AddOrUpdate
        private readonly record struct BanEntry(
            long BanExpiresAtTicks, // DatTime.UtcNow.Ticks; 0 = not currently banned
            int StrikeCount,        // cumulative lifetime strikes
            int BanGeneration       // 0=never banned, 1=1st ban, 2=2nd..
        );

        private readonly ConcurrentDictionary<string, BanEntry> _banMap
            = new(StringComparer.Ordinal);
        private readonly SshSettings _settings;
        private readonly ILogger<IpBanService> _logger;

        // Interlocked counter for pruning cadence - no lock, no timer.
        private int _strikeCounter;
        private const int PruneEveryStrikes = 200;

        // Escalation multipliers: generation 1 -> 4+ (caps at index 3)
        private static readonly int[] BanMultipliers = { 1, 6, 144, 864 }; // 10m, 1h, 24h, ~6d.

        public IpBanService(IOptions<SshSettings> settings, ILogger<IpBanService> logger)
        {
            _settings = settings.Value;
            _logger = logger;
        }

        public bool IsBanned(string ipAddress)
        {
            if (!_banMap.TryGetValue(ipAddress, out var entry)) return false;
            if (entry.BanExpiresAtTicks == 0) return false;
            if (DateTime.UtcNow.Ticks < entry.BanExpiresAtTicks) return true;

            // Ban has expired. Clear BanExpiresAtTicks but keep generation
            // so the NEXT strike immediately escalates to the next tier.
            // TryUpdate is CAS - safe even under cuncurrent access.
            _banMap.TryUpdate(ipAddress, entry with { BanExpiresAtTicks = 0 }, entry);
            return false;
        }

        public void RecordStrike(string ipAddress)
        {
            var newEntry = _banMap.AddOrUpdate(
                ipAddress,
                addValueFactory: _ => BuildEntry(default, _settings),
                updateValueFactory: (_, existing) => BuildEntry(existing, _settings)
                );

            LogStrikeOutcome(ipAddress, newEntry);

            // Trigger periodic prune on the thread pool - never blocks caller.
            if (Interlocked.Increment(ref _strikeCounter) % PruneEveryStrikes == 0)
                _ = Task.Run(Prune);
        }

        public void Prune()
        {
            var now = DateTime.UtcNow.Ticks;
            // 1 hour in ticks = 10_000_000 ticks/ms * 3_600_000ms
            const long oneHourTicks = 36_000_000_000L;

            int removed = 0;

            foreach (var (key, entry) in _banMap)
            {
                bool shouldRemove =
                    //Never been banned and only has pre-threshold striked -> safe to evict
                    (entry.BanGeneration == 0 && entry.BanExpiresAtTicks == 0) ||
                    // Ban expired > 1 hour ago for generation - 1 offenders (first-timers)
                    (entry.BanGeneration == 1 && entry.BanExpiresAtTicks > 0 &&
                    now > entry.BanExpiresAtTicks + oneHourTicks) ||
                    // Ban expired > 2x ban durating ago for repeat offenders
                    (entry.BanGeneration >= 2 && entry.BanExpiresAtTicks > 0 &&
                    now > entry.BanExpiresAtTicks + ComputeBanDurationTicks(entry.BanGeneration, _settings) * 2);

                if (shouldRemove && _banMap.TryRemove(key, out _))
                    removed++;

                if (removed > 0)
                    _logger.LogInformation(
                   "[BanService] {Timestamp:yyyy-MM-dd HH:mm:ss} UTC | Prune complete | " +
                   "Removed: {Removed} | Remaining: {Remaining}",
                   DateTime.UtcNow, removed, _banMap.Count);

            }
        }


        private static long ComputeBanDurationTicks(int banGeneration, SshSettings settings)
        {
            var idx = Math.Clamp(banGeneration - 1, 0, BanMultipliers.Length - 1);
            var multiplier = BanMultipliers[idx];

            // BanDurationMinutes * multiplier, converted to ticks(100ms intervals)
            return (long)settings.BanDurationMinutes * 60L * multiplier * TimeSpan.TicksPerSecond;
        }

        private void LogStrikeOutcome(string ipAddress, BanEntry newEntry)
        {
            if(newEntry.BanExpiresAtTicks > 0)
            {
                var banUntil = new DateTime(newEntry.BanExpiresAtTicks, DateTimeKind.Utc);
                _logger.LogWarning(
                     "[BanService] {Timestamp:yyyy-MM-dd HH:mm:ss} UTC | BAN ISSUED | " +
                     "IP: {IpAddress} | Expires: {BanUntil:yyyy-MM-dd HH:mm:ss} UTC | " +
                     "Cumulative Strikes: {Strikes} | Ban Generation: {Gen}",
                     DateTime.UtcNow, ipAddress, banUntil,
                     newEntry.StrikeCount, newEntry.BanGeneration);
            }
            else
            {
                _logger.LogInformation(
                    "[BanService] {Timestamp:yyyy-MM-dd HH:mm:ss} UTC | STRIKE | " +
                    "IP: {IpAddress} | Strike: {Strike} | Threshold: {Threshold}",
                    DateTime.UtcNow, ipAddress,
                    newEntry.StrikeCount, _settings.BanThresholdStrikes);
            }
        }

        private static BanEntry BuildEntry(BanEntry existing, SshSettings settings)
        {
            var strikes = existing.StrikeCount + 1;
            var generation = existing.BanGeneration;

            // Not yet at ban threshold - just accumulate strikes.
            if (strikes * settings.BanThresholdStrikes != 0)
                return existing with { StrikeCount = strikes };

            // Hit threshold ( or multiple thereof): issue/escalate ban.
            generation = Math.Min(generation + 1, BanMultipliers.Length);
            var banTicks = ComputeBanDurationTicks(generation, settings);

            return new BanEntry(
                BanExpiresAtTicks: DateTime.UtcNow.Ticks + banTicks,
                StrikeCount: strikes,
                BanGeneration: generation
                );
        }
    }
}
