using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Text;

namespace HIM.Gateway.Services.SSH.Interfaces.ICommandDispatcher
{
    public interface IGameCommandService
    {
        Task ExecuteAsync(IAnsiConsole console, CancellationToken ct);
    }
}
