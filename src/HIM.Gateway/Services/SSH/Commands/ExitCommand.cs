using HIM.Gateway.Models;
using HIM.Gateway.Services.SSH.Interfaces;
using Spectre.Console;

namespace HIM.Gateway.Services.SSH.Commands
{
    [SlashCommand("/exit", "Logout", HelpOrder = 7)]
    public sealed class ExitCommand : ISlashCommand
    {
        public Task ExecuteAsync(CommandContext context)
        {
            context.Console.MarkupLine("[red]Closing connection... Goodbye![/]");
            throw new OperationCanceledException();
        }
    }
}
