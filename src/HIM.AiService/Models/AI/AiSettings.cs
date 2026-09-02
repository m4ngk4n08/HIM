namespace HIM.AiService.Models.AI
{
    public class AiSettings
    {
        public string ChatProvider { get; set; } = string.Empty;
        public OllamaSettings Ollama { get; set; } = new();
        public GroqSettings Groq { get; set; } = new();
        public GeminiSettings Gemini { get; set; } = new();
        public KnowledgeBaseSettings KnowledgeBase { get; set; } = new();
        public Onnx Onnx { get; set; } = new();
        public SecuritySettings Security { get; set; } = new();
        public RateLimitSettings RateLimit { get; set; } = new();
        public TokenBudgetSettings TokenBudget { get; set; } = new();
    }
}
