using System.Collections.Generic;

namespace HIM.Gateway.Models
{
    // Mirrors HIM.AiService's CitationResponse contract (Task 22B). No shared library between the
    // two services (BL-6: HIM.Contracts was cut for a single-property record; a citation DTO
    // doesn't tip that balance either) - deserialized case-insensitively against the JSON the AI
    // service actually returns.
    public class CitationResult
    {
        public string Question { get; set; } = string.Empty;
        public List<CitationChunkResult> Chunks { get; set; } = new();
        public CitationTimingsResult Timings { get; set; } = new();
    }

    public class CitationChunkResult
    {
        public string Label { get; set; } = string.Empty;
        public float Score { get; set; }
        public string Preview { get; set; } = string.Empty;
    }

    public class CitationTimingsResult
    {
        public double EmbeddingMs { get; set; }
        public double SearchMs { get; set; }
        public int ChunksScanned { get; set; }
        public int ChunksReturned { get; set; }
    }
}
