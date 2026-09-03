using HIM.AiService.Models;

namespace HIM.AiService.Services.AI.Interface
{
    public interface IRagService
    {
        Task InitializeAsync();
        IAsyncEnumerable<string> AskAsync(string question, CancellationToken ct = default);

        // Task 22B: the same retrieval AskAsync uses, minus the model call, plus the scores and
        // timings AskAsync throws away. (CitationResponse? Result, string? Error) mirrors
        // TryGetContextAsync's own tuple pattern below - Error set means reject (e.g. the same
        // oversized-question cap /ask enforces), Result set means success.
        Task<(CitationResponse? Result, string? Error)> GetCitationsAsync(string question, CancellationToken ct = default);
    }
}
