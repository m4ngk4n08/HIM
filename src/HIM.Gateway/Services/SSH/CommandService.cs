using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using HIM.Gateway.Extensions;
using HIM.Gateway.Models.Knowledge;
using HIM.Gateway.Services.SSH.Interfaces;
using HIM.Gateway.Services.SSH.Interfaces.ICommandDispatcher;
using Microsoft.Extensions.Logging;
using Serilog.Context;
using Spectre.Console;

namespace HIM.Gateway.Services.SSH;

public class CommandService : ICommandService
{
    private readonly PortfolioData? _data;
    private readonly IAiClientService _aiClientService;
    private readonly IGameCommandService _gameCommandService;
    private readonly IMenuCommandService _menuCommandService;
    private readonly IStatsCommandService _statsCommandService;
    private readonly IMatrixCommandService _matrixCommandService;
    private readonly ICommandDispatcherHelper _commandDispatcherHelper;
    private readonly ITerminalLayoutService _terminalLayoutService;
    private readonly ILogger<CommandService> _logger;
    private readonly UserSessionState _sessionState;
    private readonly TimeSpan _cooldownDuration = TimeSpan.FromSeconds(3);

    public CommandService(
        IAiClientService aiClientService,
        IGameCommandService gameCommandService,
        IMenuCommandService menuCommandService,
        IStatsCommandService statsCommandService,
        IMatrixCommandService matrixCommandService,
        ICommandDispatcherHelper commandDispatcherHelper,
        ITerminalLayoutService terminalLayoutService,
        ILogger<CommandService> logger,
        IPortfolioDataProvider portfolioDataProvider,
        UserSessionState sessionState)
    {
        _aiClientService = aiClientService;
        _gameCommandService = gameCommandService;
        _menuCommandService = menuCommandService;
        _statsCommandService = statsCommandService;
        _matrixCommandService = matrixCommandService;
        _commandDispatcherHelper = commandDispatcherHelper;
        _terminalLayoutService = terminalLayoutService;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _sessionState = sessionState;
        _data = portfolioDataProvider.Data;
    }

    private void LogWithSession(string sessionId, LogLevel level, string message, params object[] args)
    {
        using (LogContext.PushProperty("SessionId", sessionId))
        using (LogContext.PushProperty("Source", "SSH"))
        {
            _logger.Log(level, message, args);
        }
    }

    public async Task ProcessCommandAsync(IAnsiConsole console, string command, Stream stream, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command)) return;

        var sessionId = _sessionState.SessionId;

        var safeCommand = SanitizerExtension.Redact(command);
        LogWithSession(sessionId, LogLevel.Information, "Command received: {Command}", safeCommand);

        if (_data == null)
        {
            console.MarkupLine("[red]Error:[/] Knowledge base file not found or corrupted.");
            return;
        }

        await _terminalLayoutService.InitializeTerminalLayoutAsync(console, stream, ct);

        try
        {
            switch (command.ToLower())
            {
                case "/help":
                    ShowHelp(console);
                    break;

                case "/clear":
                    await _terminalLayoutService.InitializeTerminalLayoutAsync(console, stream, ct);
                    break;

                case "/menu":
                    await _menuCommandService.ExecuteAsync(console, stream, _data, ct);
                    break;

                case "/stats":
                    await _statsCommandService.ExecuteAsync(console, stream, _data, ct);
                    break;

                case "/matrix":
                    await _matrixCommandService.ExecuteAsync(console, stream, ct);
                    break;

                case "/game":
                    await _gameCommandService.ExecuteAsync(console, stream, ct);
                    break;

                case "/exit":
                    console.MarkupLine("[red]Closing connection... Goodbye![/]");
                    throw new OperationCanceledException();

                default:
                    if(command.ToLower().StartsWith("/theme"))
                    {
                        HandleThemeCommand(console, command);
                        break;
                    }

                    if (IsRateLimited(_sessionState))
                    {
                        console.MarkupLine($"[yellow]![/] [grey]{Markup.Escape("Neural Link is cooling down.. please wait")}[/]");
                        break;
                    }
                    await HandleAiChatAsync(console, command, sessionId, ct);
                    break;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogWithSession(sessionId, LogLevel.Error, "Error executing command {Command}: {Error}", safeCommand, ex.Message);
            console.MarkupLine($"[red]Error: {Markup.Escape(ex.Message)}[/]");
        }
    }

    private bool IsRateLimited(UserSessionState state)
    {
        var now = DateTime.UtcNow;
        if (now - state.LastQuery < _cooldownDuration)
            return true;
        state.LastQuery = now;
        return false;
    }
    private async Task HandleAiChatAsync(IAnsiConsole console, string question, string sessionId, CancellationToken ct)
    {
        console.WriteLine();
        console.Write(new Markup("[cyan1]AI:[/] "));

        var stopwatch = Stopwatch.StartNew();
        StringBuilder? responseBuilder = _logger.IsEnabled(LogLevel.Debug) ? new StringBuilder() : null;

        try
        {
            // SEC-02: the egress filter is the last boundary before the visitor - redact here,
            // on the stream that actually reaches the console, not only on what gets logged below.
            var responsesStream = _aiClientService.GetAiResponseAsync(question, ct, sessionId)
                .RedactPiiAsync(ct);
            await using var enumerator = responsesStream.GetAsyncEnumerator(ct);

            bool hasData = await console.Status()
                .Spinner(Spinner.Known.Dots)
                .SpinnerStyle(Style.Parse("cyan1"))
                .StartAsync("Thinking..", async ctx =>
                {
                    return await enumerator.MoveNextAsync();
                });

            if (hasData)
            {
                // Accumulate first chunk
                var firstChunk = enumerator.Current;
                responseBuilder?.Append(firstChunk);

                while (await enumerator.MoveNextAsync())
                {
                    var chunk = enumerator.Current;
                    responseBuilder?.Append(chunk);
                    await Task.Delay(20, ct);
                }
            }

            stopwatch.Stop();
            var fullResponse = responseBuilder?.ToString() ?? "No response received.";

            // Render the response as a panel only – no raw text written outside
            console.WriteLine();
            var panel = new Panel(new Text(fullResponse))
            {
                Header = new PanelHeader($"🤖 AI • {stopwatch.ElapsedMilliseconds}ms", Justify.Left),
                Border = BoxBorder.Rounded,
                BorderStyle = new Style(ThemeService.PrimaryColor),
                Padding = new Padding(1, 1)
            };
            console.Write(panel);

            // Logging (unchanged)
            var safeQuestion = SanitizerExtension.Redact(question);
            LogWithSession(sessionId, LogLevel.Information,
                "SSH AI chat completed. Question: {Question}, ResponseLength: {Length}, Duration: {Duration}ms",
                safeQuestion, fullResponse.Length, stopwatch.ElapsedMilliseconds);

            if (_logger.IsEnabled(LogLevel.Debug) && responseBuilder != null)
            {
                var safeResponse = SanitizerExtension.Redact(fullResponse);
                LogWithSession(sessionId, LogLevel.Debug, "SSH AI response: {Response}", safeResponse);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogWithSession(sessionId, LogLevel.Error, "Error during AI chat: {Error}", ex.Message);
            var safeMessage = ex.Message.EscapeMarkup();
            console.MarkupLine($"[red]Error: {safeMessage}[/]");
        }
        finally
        {
            console.WriteLine();
            console.WriteLine();
        }
    }
    private void HandleThemeCommand(IAnsiConsole console, string command)
    {
        var parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            console.MarkupLine($"[{ThemeService.PrimaryColor}]Current theme: {ThemeService.CurrentTheme}[/]");
            console.MarkupLine("[grey]Usage: /theme [dark|neon|retro][/]");
            return;
        }

        var themeName = parts[1].ToLower();
        var newTheme = themeName switch
        {
            "neon" => Theme.Neon,
            "retro" => Theme.Retro,
            _ => Theme.Dark
        };

        ThemeService.SetTheme(newTheme);
        console.MarkupLine($"[green]✓ Theme switched to {newTheme}![/]");
        console.MarkupLine("[grey]Type /clear to refresh the UI.[/]");
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