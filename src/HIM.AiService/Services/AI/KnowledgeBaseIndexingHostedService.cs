using HIM.AiService.Services.AI.Interface;

namespace HIM.AiService.Services.AI
{
    /// <summary>
    /// Runs knowledge-base indexing as a background task instead of inline in Program.cs, so
    /// host startup (and /health/live) is never blocked on it. BackgroundService.StartAsync does
    /// not await ExecuteAsync to completion, only kicks it off - readiness flips only once
    /// indexing actually finishes, and a failure here surfaces as not-ready instead of a
    /// half-initialised service answering queries against an empty index.
    /// </summary>
    public class KnowledgeBaseIndexingHostedService : BackgroundService
    {
        private readonly IKnowledgeBaseService _kbService;
        private readonly KnowledgeBaseReadinessState _state;
        private readonly ILogger<KnowledgeBaseIndexingHostedService> _logger;

        public KnowledgeBaseIndexingHostedService(
            IKnowledgeBaseService kbService,
            KnowledgeBaseReadinessState state,
            ILogger<KnowledgeBaseIndexingHostedService> logger)
        {
            _kbService = kbService;
            _state = state;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                await _kbService.InitializeAsync();
                _state.MarkReady();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Knowledge base indexing failed; readiness will report unhealthy.");
                _state.MarkFailed(ex.Message);
            }
        }
    }
}
