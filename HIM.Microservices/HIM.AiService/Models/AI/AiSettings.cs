namespace HIM.AiService.Models.AI
{
    public class AiSettings
    {
        public OllamaSettings Ollama { get; set; } = new();
        public KnowledgeBaseSettings KnowledgeBase { get; set; } = new();
    }
}
