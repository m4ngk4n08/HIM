using HIM.AiService.Services;
using HIM.AiService.Services.AI;
using HIM.AiService.Services.AI.Interface;

namespace HIM.AiService.Extenstions
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
            services.AddScoped<IRagService, RagService>();

            return services;
        }
    }
}
