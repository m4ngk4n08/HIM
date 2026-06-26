using Spectre.Console;
using System.IO;

namespace HIM.Gateway.Services.SSH.Interfaces
{
    public interface ITerminalLayoutService
    {
        Task InitializeTerminalLayoutAsync(IAnsiConsole console, Stream stream, CancellationToken ct);
    }
}
