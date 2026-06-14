namespace HIM.AiService.Models.AI
{
    public class AiSettings
    {
        public string ChatProvider { get; set; }
        public OllamaSettings Ollama { get; set; } = new();
        public GroqSettings Groq { get; set; } = new();
        public KnowledgeBaseSettings KnowledgeBase { get; set; } = new();
        public Onnx Onnx { get; set; } = new();
    }
}
