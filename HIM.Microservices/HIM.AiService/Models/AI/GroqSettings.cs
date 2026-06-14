namespace HIM.AiService.Models.AI
{
    public class GroqSettings
    {
        public string ApiKey { get; set; } = string.Empty;
        public string ModelId { get; set; } = "llama-3.1-70b-versatile";

        public string Endpoint { get; set; } = string.Empty;
    }
}
