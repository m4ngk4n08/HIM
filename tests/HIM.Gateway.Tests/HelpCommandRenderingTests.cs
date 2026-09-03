using HIM.Gateway.Models;
using HIM.Gateway.Models.Knowledge;
using HIM.Gateway.Services.SSH;
using HIM.Gateway.Services.SSH.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;

namespace HIM.Gateway.Tests;

/// <summary>
/// Task 18E's exit gate: "/help rendered output is byte-identical to 04077d9's." This pins that
/// by rebuilding the table exactly as CommandService.ShowHelp hardcoded it before the refactor
/// (same border, title, columns, escaping, row order) and diffing the two renders directly,
/// rather than eyeballing either one.
/// </summary>
public class HelpCommandRenderingTests
{
    private static string RenderOriginalHardcodedTable(IThemeService theme)
    {
        var table = new Table();
        table.Border(TableBorder.Rounded);
        table.BorderColor(theme.PrimaryColor);
        table.Title = new TableTitle(" COMMANDS ", new Style(theme.PrimaryColor));

        table.AddColumn(new TableColumn("Command").Centered().NoWrap());
        table.AddColumn(new TableColumn("Description").NoWrap());

        table.AddRow(Markup.Escape("/menu"), Markup.Escape("Interactive navigation menu"))
             .AddRow(Markup.Escape("/stats"), Markup.Escape("Developer RPG stats sheet"))
             .AddRow(Markup.Escape("/matrix"), Markup.Escape("Digital rain animation"))
             .AddRow(Markup.Escape("/game"), Markup.Escape("Developer trivia game"))
             .AddRow(Markup.Escape("/theme [dark|neon|retro]"), Markup.Escape("Change UI theme"))
             .AddRow(Markup.Escape("/clear"), Markup.Escape("Clear screen"))
             .AddRow(Markup.Escape("/exit"), Markup.Escape("Logout"))
             .AddRow(Markup.Escape("/cite"), Markup.Escape("Show which knowledge-base chunks answered your last question"))
             .AddRow(Markup.Escape("/defense"), Markup.Escape("Live view of the 8-layer connection defense pipeline"))
             .AddRow(Markup.Escape("/who"), Markup.Escape("See who else is connected right now"));

        return RenderToString(table);
    }

    private static string RenderToString(Table table)
    {
        var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(writer)
        });
        console.Write(table);
        return writer.ToString();
    }

    [Fact]
    public async Task HelpCommand_RenderedOutput_IsByteIdenticalToTheOriginalHardcodedTable()
    {
        using var provider = GatewayServiceProviderFactory.Build();
        using var scope = provider.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<ISlashCommandRegistry>();
        var theme = scope.ServiceProvider.GetRequiredService<IThemeService>();
        Assert.True(registry.TryGet("/help", out var help));

        var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(writer)
        });

        using var stream = new MemoryStream();
        var context = new CommandContext(console, stream, "/help", new PortfolioData(), "session", CancellationToken.None);
        await help.ExecuteAsync(context);

        Assert.Equal(RenderOriginalHardcodedTable(theme), writer.ToString());
    }
}
