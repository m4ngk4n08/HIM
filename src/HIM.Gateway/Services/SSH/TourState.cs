namespace HIM.Gateway.Services.SSH
{
    /// <summary>
    /// The three audience paths /tour supports, from plans/plan-tour.md and
    /// plans/guide-plan-tour.md - kept from those older plans, unlike their design.
    /// </summary>
    public enum TourMode
    {
        Quick,
        Recruiter,
        Engineer
    }

    /// <summary>
    /// Per-session /tour state: which mode was picked, which step the visitor is on, and whether
    /// a tour is running right now. Registered Scoped - one instance per SSH shell channel,
    /// resolved from the same scope as UserSessionState. A Singleton here would put every visitor
    /// on one person's tour step, the same class of bug as BL-10 (the static ThemeService) and
    /// the PacManGame singleton before Task 21 made game state scoped - see ServiceLifetimeTests.
    /// No ConditionalWeakTable: that was a workaround for having nowhere to put per-session state
    /// before scoped DI existed here, and it no longer applies.
    /// </summary>
    public sealed class TourState
    {
        public TourMode Mode { get; set; }
        public int CurrentStepIndex { get; set; }
        public bool IsActive { get; set; }
    }
}
