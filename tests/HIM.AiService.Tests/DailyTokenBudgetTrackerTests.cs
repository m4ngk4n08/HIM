using HIM.AiService.Models.AI;
using HIM.AiService.Services.AI;
using Microsoft.Extensions.Options;

namespace HIM.AiService.Tests;

/// <summary>
/// Task 14E (SEC-04): the global daily token ceiling, owned and counted by the AI service
/// itself. Uses the internal clock seam so day-rollover can be tested without waiting on the
/// real clock.
/// </summary>
public class DailyTokenBudgetTrackerTests
{
    private static DailyTokenBudgetTracker CreateTracker(int ceiling, DateTime utcNow) =>
        new(Options.Create(new AiSettings { TokenBudget = new TokenBudgetSettings { DailyTokenCeiling = ceiling } }),
            () => utcNow);

    [Fact]
    public void IsExhausted_IsFalse_BeforeAnyUsage()
    {
        var tracker = CreateTracker(ceiling: 100, DateTime.UtcNow);
        Assert.False(tracker.IsExhausted);
    }

    [Fact]
    public void IsExhausted_BecomesTrue_OnceUsageReachesTheCeiling()
    {
        var tracker = CreateTracker(ceiling: 100, DateTime.UtcNow);

        tracker.RecordUsage(60);
        Assert.False(tracker.IsExhausted);

        tracker.RecordUsage(40);
        Assert.True(tracker.IsExhausted);
    }

    [Fact]
    public void RecordUsage_Accumulates_AcrossMultipleCalls()
    {
        var tracker = CreateTracker(ceiling: 1000, DateTime.UtcNow);

        tracker.RecordUsage(100);
        tracker.RecordUsage(250);

        Assert.Equal(350, tracker.TokensUsedToday);
    }

    [Fact]
    public void Usage_Resets_WhenTheUtcCalendarDayRollsOver()
    {
        var day1 = new DateTime(2026, 9, 2, 23, 0, 0, DateTimeKind.Utc);
        var day2 = new DateTime(2026, 9, 3, 1, 0, 0, DateTimeKind.Utc);
        var now = day1;

        var tracker = new DailyTokenBudgetTracker(
            Options.Create(new AiSettings { TokenBudget = new TokenBudgetSettings { DailyTokenCeiling = 100 } }),
            () => now);

        tracker.RecordUsage(100);
        Assert.True(tracker.IsExhausted);

        now = day2;

        Assert.False(tracker.IsExhausted);
        Assert.Equal(0, tracker.TokensUsedToday);
    }
}
