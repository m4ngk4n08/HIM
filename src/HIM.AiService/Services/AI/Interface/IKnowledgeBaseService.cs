using HIM.AiService.Models.AI;

namespace HIM.AiService.Services.AI.Interface
{
    public interface IKnowledgeBaseService
    {
        Task InitializeAsync();
        Task<List<KnowledgeChunks>> SearchAsync(float[] queryEmbedding, int topK = 3);
    }
}
