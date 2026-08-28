using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Text;

namespace HIM.Gateway.Services.SSH.Interfaces.ICommandDispatcher
{
    public interface IMatrixCommandService
    {
        Task ExecuteAsync(IAnsiConsole console, Stream stream, CancellationToken ct);
    }
}
