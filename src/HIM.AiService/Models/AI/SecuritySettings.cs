namespace HIM.AiService.Models.AI
{
    public class SecuritySettings
    {
        public string SharedSecret { get; set; } = string.Empty;

        // SEC-05: a question longer than this never reaches the model - rejected up front so
        // an oversized/injection-shaped payload can't ride along as prompt content.
        public int MaxQuestionLength { get; set; } = 500;
    }
}
