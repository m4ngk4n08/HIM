using HIM.Gateway.Models;
using HIM.Gateway.Services.SSH.Interfaces;

namespace HIM.Gateway.Services.SSH.Commands
{
    [SlashCommand("/clear", "Clear screen", HelpOrder = 6)]
    public sealed class ClearCommand : ISlashCommand
    {
        private readonly ITerminalLayoutService _terminalLayoutService;

        public ClearCommand(ITerminalLayoutService terminalLayoutService)
        {
            _terminalLayoutService = terminalLayoutService;
        }

        public Task ExecuteAsync(CommandContext context) =>
            _terminalLayoutService.InitializeTerminalLayoutAsync(context.Console, context.Stream, context.Ct);
    }
}
