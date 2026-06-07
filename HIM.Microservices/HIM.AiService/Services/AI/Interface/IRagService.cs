namespace HIM.AiService.Services.AI.Interface
{
    public interface IRagService
    {
        Task InitializeAsync();
        Task<string> AskAsync(string question);
    }
}
