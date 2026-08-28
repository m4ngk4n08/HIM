using Spectre.Console;
using System.Text;

namespace HIM.Gateway.Services.SSH.Interfaces.ICommandDispatcher
{
    public interface ICommandDispatcherHelper
    {
        Task<string> ReadInputManualAsync(IAnsiConsole console, Stream stream, CancellationToken ct);
        Task SetScrollingRegionAsync(Stream stream, int top, int bottom, CancellationToken ct);
        Task MoveCursorAsync(Stream stream, int row, int col, CancellationToken ct);
    }
}
