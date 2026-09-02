namespace HIM.AiService.Services.AI
{
    /// <summary>
    /// Shared readiness flag between <see cref="KnowledgeBaseIndexingHostedService"/> (the writer)
    /// and the readiness health check (the reader). Registered as a singleton so both sides see
    /// the same instance regardless of DI scope.
    /// </summary>
    public class KnowledgeBaseReadinessState
    {
        private volatile bool _isReady;
        private volatile bool _failed;
        private string? _failureReason;

        public bool IsReady => _isReady;
        public bool Failed => _failed;
        public string? FailureReason => _failureReason;

        public void MarkReady() => _isReady = true;

        public void MarkFailed(string reason)
        {
            _failed = true;
            _failureReason = reason;
        }
    }
}
