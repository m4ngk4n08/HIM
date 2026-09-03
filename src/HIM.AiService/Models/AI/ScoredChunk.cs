namespace HIM.AiService.Models.AI
{
    /// <summary>
    /// A retrieved chunk paired with the similarity score SearchAsync computes but discards.
    /// Introduced for Task 22B (/api/chat/cite) - the search loop itself is unchanged, just no
    /// longer throws the score away before returning.
    /// </summary>
    public class ScoredChunk
    {
        public KnowledgeChunks Chunk { get; set; } = new();
        public float Score { get; set; }
    }
}
