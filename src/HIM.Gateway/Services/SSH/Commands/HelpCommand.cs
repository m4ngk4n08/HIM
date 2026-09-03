using HIM.Gateway.Models;
using HIM.Gateway.Services.SSH.Interfaces;
using Spectre.Console;

namespace HIM.Gateway.Services.SSH.Commands
{
    [SlashCommand("/help", "Show this list of commands", HelpOrder = 0)]
    public sealed class HelpCommand : ISlashCommand
    {
        private readonly ISlashCommandRegistry _commandRegistry;
        private readonly IThemeService _theme;

        public HelpCommand(ISlashCommandRegistry commandRegistry, IThemeService theme)
        {
            _commandRegistry = commandRegistry;
            _theme = theme;
        }

        public Task ExecuteAsync(CommandContext context)
        {
            ShowHelp(context.Console);
            return Task.CompletedTask;
        }

        // 🔧 Fixed: all cell content is escaped to prevent markup errors
        private void ShowHelp(IAnsiConsole console)
        {
            var table = new Table();
            table.Border(TableBorder.Rounded);
            table.BorderColor(_theme.PrimaryColor);
            table.Title = new TableTitle(" COMMANDS ", new Style(_theme.PrimaryColor));

            table.AddColumn(new TableColumn("Command").Centered().NoWrap());
            table.AddColumn(new TableColumn("Description").NoWrap());

            // Escape every string to ensure no markup is parsed. /help doesn't list itself -
            // that's how today's hardcoded table behaves, so the same descriptor set that
            // routes /help is filtered by HandlerType, not re-typed, when rendering it.
            foreach (var descriptor in _commandRegistry.Descriptors)
            {
                if (descriptor.HandlerType == typeof(HelpCommand)) continue;
                table.AddRow(Markup.Escape(descriptor.Usage), Markup.Escape(descriptor.Description));
            }

            console.Write(table);
        }
    }
}
