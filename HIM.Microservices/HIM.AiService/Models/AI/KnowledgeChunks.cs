namespace HIM.AiService.Models.AI
{
    public class KnowledgeChunks
    {
        public string Text { get; set; } = string.Empty;
        public float[] Vector { get; set; } = Array.Empty<float>();
    }
}
