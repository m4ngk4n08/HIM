using HIM.Gateway.Models;
using HIM.Gateway.Services.SSH.Interfaces;
using HIM.Gateway.Services.SSH.Interfaces.ICommandDispatcher;

namespace HIM.Gateway.Services.SSH.Commands
{
    [SlashCommand("/stats", "Developer RPG stats sheet", HelpOrder = 2)]
    public sealed class StatsCommand : ISlashCommand
    {
        private readonly IStatsCommandService _statsCommandService;

        public StatsCommand(IStatsCommandService statsCommandService)
        {
            _statsCommandService = statsCommandService;
        }

        public Task ExecuteAsync(CommandContext context) =>
            _statsCommandService.ExecuteAsync(context.Console, context.Stream, context.Data, context.Ct);
    }
}
