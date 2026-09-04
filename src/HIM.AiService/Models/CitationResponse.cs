namespace HIM.AiService.Models
{
    public class CitationResponse
    {
        public string Question { get; set; } = string.Empty;
        public List<CitationChunk> Chunks { get; set; } = new();
        public CitationTimings Timings { get; set; } = new();
    }

    public class CitationChunk
    {
        public string Label { get; set; } = string.Empty;
        public float Score { get; set; }
        public string Preview { get; set; } = string.Empty;

        // Task 27A: the whole chunk, everything after the section label, untrimmed - unlike
        // Preview, which stays capped at PreviewMaxLength on purpose (see RagService). This is
        // what lets the gateway's /cite <n> show a source in full without a second network call.
        public string FullText { get; set; } = string.Empty;
    }

    public class CitationTimings
    {
        public double EmbeddingMs { get; set; }
        public double SearchMs { get; set; }
        public int ChunksScanned { get; set; }
        public int ChunksReturned { get; set; }
    }
}
