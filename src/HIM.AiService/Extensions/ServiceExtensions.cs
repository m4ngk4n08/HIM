using HIM.AiService.Services;
using HIM.AiService.Services.AI;
using HIM.AiService.Services.AI.Interface;

namespace HIM.AiService.Extensions
{
    public static class ServiceExtensions
    {
        public static IServiceCollection AddRepositories(this IServiceCollection services)
        {
            // Future repository registrations
            return services;
        }

        public static IServiceCollection AddServices(this IServiceCollection services)
        {
            // Infrastructure
            services.AddHttpClient();

            // AI Services
            services.AddSingleton<IEmbeddingService, EmbeddingService>();
            services.AddSingleton<IVectorSearchService, VectorSearchService>();
            services.AddSingleton<IKnowledgeBaseService, KnowledgeBaseService>();
            services.AddSingleton<DailyTokenBudgetTracker>();
            services.AddScoped<IRagService, RagService>();

            // SEC-08: indexing runs as a hosted service instead of inline in Program.cs, and
            // readiness flips only once it completes (or reports unhealthy if it fails).
            services.AddSingleton<KnowledgeBaseReadinessState>();
            services.AddHostedService<KnowledgeBaseIndexingHostedService>();
            services.AddHealthChecks()
                .AddCheck<KnowledgeBaseReadinessCheck>("knowledge_base", tags: new[] { "ready" });

            return services;
        }
    }
}
