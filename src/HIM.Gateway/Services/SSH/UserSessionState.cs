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
    }
}
