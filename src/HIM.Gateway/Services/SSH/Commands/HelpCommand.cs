using HIM.Gateway.Models;
using HIM.Gateway.Services.SSH.Interfaces;
using Spectre.Console;

namespace HIM.Gateway.Services.SSH.Commands
{
    [SlashCommand("/help", "Show this list of commands", HelpOrder = 0)]
    public sealed class HelpCommand : ISlashCommand
    {
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
            table.BorderColor(ThemeService.PrimaryColor);
            table.Title = new TableTitle(" COMMANDS ", new Style(ThemeService.PrimaryColor));

            table.AddColumn(new TableColumn("Command").Centered().NoWrap());
            table.AddColumn(new TableColumn("Description").NoWrap());

            // Escape every string to ensure no markup is parsed
            table.AddRow(Markup.Escape("/menu"), Markup.Escape("Interactive navigation menu"))
                 .AddRow(Markup.Escape("/stats"), Markup.Escape("Developer RPG stats sheet"))
                 .AddRow(Markup.Escape("/matrix"), Markup.Escape("Digital rain animation"))
                 .AddRow(Markup.Escape("/game"), Markup.Escape("Developer trivia game"))
                 .AddRow(Markup.Escape("/theme [dark|neon|retro]"), Markup.Escape("Change UI theme"))
                 .AddRow(Markup.Escape("/clear"), Markup.Escape("Clear screen"))
                 .AddRow(Markup.Escape("/exit"), Markup.Escape("Logout"));

            console.Write(table);
        }
    }
}
