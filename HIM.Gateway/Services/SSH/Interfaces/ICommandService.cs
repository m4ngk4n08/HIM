using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Text;

namespace HIM.Gateway.Services.SSH.Interfaces
{
    public interface ICommandService
    {
        Task ProcessCommandAsync(IAnsiConsole console, string question, CancellationToken ct);
    }
}
