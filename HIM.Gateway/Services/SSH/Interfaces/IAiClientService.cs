using System;
using System.Collections.Generic;
using System.Text;

namespace HIM.Gateway.Services.SSH.Interfaces
{
    public interface IAiClientService
    {
        IAsyncEnumerable<string> GetAiResponseAsync(string question, CancellationToken ct);
    }
}
