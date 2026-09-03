using HIM.Gateway.Models.Knowledge;
using Spectre.Console;

namespace HIM.Gateway.Models
{
    /// <summary>
    /// Everything a slash command handler needs to run, gathered so the four pre-existing
    /// I*CommandService handlers (whose ExecuteAsync signatures disagree on which of these they
    /// take) can be adapted to one shape without changing those services themselves.
    /// </summary>
    public sealed record CommandContext(
        IAnsiConsole Console,
        Stream Stream,
        string RawCommand,
        PortfolioData Data,
        string SessionId,
        CancellationToken Ct);
}
