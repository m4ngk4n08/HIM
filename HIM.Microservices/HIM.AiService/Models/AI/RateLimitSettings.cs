namespace HIM.AiService.Models.AI
{
    public class RateLimitSettings
    {
        public int PermitLimit { get; set; } = 20;
        public int WindowSeconds { get; set; } = 60;
        public int QueueLimit { get; set; } = 0;
    }
}
