using HIM.Gateway.Models;

namespace HIM.Gateway.Services.SSH
{
    /// <summary>
    /// Per-session state: the AI-chat cooldown timer and a correlation ID for logging.
    /// Registered Scoped - one instance per SSH shell channel, resolved from the scope
    /// created in SshServerListener.HandleShellChannelAsync.
    /// </summary>
    public class UserSessionState
    {
        public DateTime LastQuery { get; set; }
        public string SessionId { get; } = Guid.NewGuid().ToString();

        // SEC-04: per-session AI query budget, checked against SshSettings.MaxAiQueriesPerSession.
        public int AiQueryCount { get; set; }

        // Task 22C: the question /cite explains. Set only when a question actually reaches the
        // AI (not on a recognized slash command, not on a rate-limited or budget-exhausted
        // attempt), so /cite always describes what was really asked, not the raw input.
        public string? LastQuestion { get; set; }

        // Task 23A: caches the last successful /cite result against the question it answered, so a
        // repeat /cite for the same LastQuestion never re-hits the AI service. The question lives
        // inside the cache entry (not compared against LastQuestion via a separate field) so a new
        // question can't accidentally render the previous one's citations. Never set on an error
        // result - a transient failure must not stick for the rest of the session.
        public CachedCitation? CachedCitation { get; set; }
    }

    public sealed record CachedCitation(string Question, CitationResult Result);
}
