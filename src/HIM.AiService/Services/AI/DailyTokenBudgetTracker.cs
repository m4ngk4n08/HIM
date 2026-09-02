using HIM.AiService.Models.AI;
using Microsoft.Extensions.Options;

namespace HIM.AiService.Services.AI
{
    /// <summary>
    /// SEC-04: a global (not per-session) daily token ceiling, owned and counted by the AI
    /// service itself - registered as a singleton so every request shares the same counter.
    /// Resets automatically when the UTC calendar day rolls over.
    /// </summary>
    public class DailyTokenBudgetTracker
    {
        private readonly object _lock = new();
        private readonly int _dailyCeiling;
        private readonly Func<DateTime> _utcNow;

        private DateOnly _day;
        private long _tokensUsedToday;

        public DailyTokenBudgetTracker(IOptions<AiSettings> settings)
            : this(settings, () => DateTime.UtcNow)
        {
        }

        // Internal seam for tests to control day rollover without waiting on the real clock.
        internal DailyTokenBudgetTracker(IOptions<AiSettings> settings, Func<DateTime> utcNow)
        {
            _dailyCeiling = settings.Value.TokenBudget.DailyTokenCeiling;
            _utcNow = utcNow;
            _day = DateOnly.FromDateTime(_utcNow());
        }

        public bool IsExhausted
        {
            get
            {
                lock (_lock)
                {
                    RolloverIfNewDay();
                    return _tokensUsedToday >= _dailyCeiling;
                }
            }
        }

        public long TokensUsedToday
        {
            get
            {
                lock (_lock)
                {
                    RolloverIfNewDay();
                    return _tokensUsedToday;
                }
            }
        }

        public void RecordUsage(int estimatedTokens)
        {
            if (estimatedTokens <= 0) return;

            lock (_lock)
            {
                RolloverIfNewDay();
                _tokensUsedToday += estimatedTokens;
            }
        }

        private void RolloverIfNewDay()
        {
            var today = DateOnly.FromDateTime(_utcNow());
            if (today != _day)
            {
                _day = today;
                _tokensUsedToday = 0;
            }
        }
    }
}
