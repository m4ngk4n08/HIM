using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Text;

namespace HIM.Gateway.Services.SSH.Interfaces.ICommandDispatcher
{
    public interface ICommandDispatcherHelper
    {
        Task<string> ReadInputManualAsync(IAnsiConsole console, Stream stream, CancellationToken ct);
    }
}
