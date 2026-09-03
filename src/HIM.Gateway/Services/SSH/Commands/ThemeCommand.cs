using HIM.Gateway.Models;
using HIM.Gateway.Services.SSH.Interfaces;
using Spectre.Console;

namespace HIM.Gateway.Services.SSH.Commands
{
    [SlashCommand("/theme", "Change UI theme", Usage = "/theme [dark|neon|retro]", HelpOrder = 5)]
    public sealed class ThemeCommand : ISlashCommand
    {
        private readonly IThemeService _theme;

        public ThemeCommand(IThemeService theme)
        {
            _theme = theme;
        }

        public Task ExecuteAsync(CommandContext context)
        {
            var console = context.Console;
            var parts = context.RawCommand.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                console.MarkupLine($"[{_theme.PrimaryColor}]Current theme: {_theme.CurrentTheme}[/]");
                console.MarkupLine("[grey]Usage: /theme [dark|neon|retro][/]");
                return Task.CompletedTask;
            }

            var themeName = parts[1].ToLower();
            var newTheme = themeName switch
            {
                "neon" => Theme.Neon,
                "retro" => Theme.Retro,
                _ => Theme.Dark
            };

            _theme.SetTheme(newTheme);
            console.MarkupLine($"[green]✓ Theme switched to {newTheme}![/]");
            console.MarkupLine("[grey]Type /clear to refresh the UI.[/]");
            return Task.CompletedTask;
        }
    }
}
