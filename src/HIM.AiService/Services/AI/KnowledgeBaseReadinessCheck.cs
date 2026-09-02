using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HIM.AiService.Services.AI
{
    /// <summary>
    /// Backs /health/ready. Distinct from /health/live on purpose: the process can be up (live)
    /// while the knowledge base is still indexing or failed to index (not ready) - a readiness
    /// probe that reports healthy during that window is worse than no probe at all.
    /// </summary>
    public class KnowledgeBaseReadinessCheck : IHealthCheck
    {
        private readonly KnowledgeBaseReadinessState _state;

        public KnowledgeBaseReadinessCheck(KnowledgeBaseReadinessState state)
        {
            _state = state;
        }

        public Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            if (_state.Failed)
            {
                return Task.FromResult(HealthCheckResult.Unhealthy(
                    $"Knowledge base indexing failed: {_state.FailureReason}"));
            }

            return Task.FromResult(_state.IsReady
                ? HealthCheckResult.Healthy("Knowledge base indexed and searchable.")
                : HealthCheckResult.Unhealthy("Knowledge base is still indexing."));
        }
    }
}
