namespace HIM.AiService.Models.AI
{
    public class KnowledgeBaseSettings
    {
        public string FilePath { get; set; } = string.Empty;
        public string MemoryCollectionName { get; set; } = string.Empty;

        public string CacheFile { get; set; } = string.Empty;

        // Cosine similarity cutoff (dot product of L2-normalized vectors, so already
        // cosine-equivalent) below which a retrieved chunk is dropped rather than pasted into
        // the prompt regardless of rank. topK stays an upper bound, not a guarantee of relevance.
        public float MinSimilarityScore { get; set; } = 0.3f;
    }
}
