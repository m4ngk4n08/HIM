using HIM.Gateway.Models;
using HIM.Gateway.Services.SSH.Interfaces;
using HIM.Gateway.Services.SSH.Interfaces.ICommandDispatcher;

namespace HIM.Gateway.Services.SSH.Commands
{
    [SlashCommand("/game", "Developer trivia game", HelpOrder = 4)]
    public sealed class GameCommand : ISlashCommand
    {
        private readonly IGameCommandService _gameCommandService;

        public GameCommand(IGameCommandService gameCommandService)
        {
            _gameCommandService = gameCommandService;
        }

        public Task ExecuteAsync(CommandContext context) =>
            _gameCommandService.ExecuteAsync(context.Console, context.Stream, context.Ct);
    }
}
