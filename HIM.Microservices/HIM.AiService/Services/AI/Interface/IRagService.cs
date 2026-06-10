namespace HIM.AiService.Services.AI.Interface
{
    public interface IRagService
    {
        Task InitializeAsync();
        IAsyncEnumerable<string> AskAsync(string question, CancellationToken ct = default);
    }
}
