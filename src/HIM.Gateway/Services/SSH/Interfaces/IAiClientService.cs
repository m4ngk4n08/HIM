using HIM.Gateway.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace HIM.Gateway.Services.SSH.Interfaces
{
    public interface IAiClientService
    {
        IAsyncEnumerable<string> GetAiResponseAsync(string question, CancellationToken ct, string? correlationId = null);

        // Task 22C: backs /cite. Result set on success; Error set (Result null) on rejection -
        // e.g. the AI service's own MaxQuestionLength cap - or a transport failure.
        Task<(CitationResult? Result, string? Error)> GetCitationsAsync(string question, CancellationToken ct, string? correlationId = null);
    }
}
