using HIM.AiService.Models.AI;

namespace HIM.AiService.Services.AI.Interface
{
    public interface IKnowledgeBaseService
    {
        Task InitializeAsync();
        Task<List<KnowledgeChunks>> SearchAsync(float[] queryEmbedding, int topK = 3, float minScore = float.NegativeInfinity);

        // Task 22B: same retrieval as SearchAsync, minus the part where it throws the similarity
        // score away. SearchAsync's own signature is untouched - existing callers (and
        // OpenerQuestionsRetrievalTests, RetrievalThresholdTests, DeveloperToolsFocusRetrievalTests)
        // are unaffected. ChunksScanned is the total chunk count searched, before the minScore cutoff.
        Task<(List<ScoredChunk> Results, int ChunksScanned)> SearchWithScoresAsync(float[] queryEmbedding, int topK = 3, float minScore = float.NegativeInfinity);
    }
}
