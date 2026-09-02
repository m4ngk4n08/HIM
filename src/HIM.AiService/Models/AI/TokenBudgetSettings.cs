namespace HIM.AiService.Models.AI
{
    public class TokenBudgetSettings
    {
        // SEC-04: once this many (estimated) tokens have been spent on chat completions today
        // (UTC), the service stops calling the model and serves a static knowledge-base answer
        // instead - a visitor still gets a useful reply, not an error or a dead prompt.
        public int DailyTokenCeiling { get; set; } = 200_000;
    }
}
