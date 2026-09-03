using HIM.Gateway.Models;
using HIM.Gateway.Services.SSH.Interfaces;
using HIM.Gateway.Services.SSH.Interfaces.ICommandDispatcher;

namespace HIM.Gateway.Services.SSH.Commands
{
    [SlashCommand("/menu", "Interactive navigation menu", HelpOrder = 1)]
    public sealed class MenuCommand : ISlashCommand
    {
        private readonly IMenuCommandService _menuCommandService;

        public MenuCommand(IMenuCommandService menuCommandService)
        {
            _menuCommandService = menuCommandService;
        }

        public Task ExecuteAsync(CommandContext context) =>
            _menuCommandService.ExecuteAsync(context.Console, context.Stream, context.Data, context.Ct);
    }
}
