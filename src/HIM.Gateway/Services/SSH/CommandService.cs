using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using HIM.Gateway.Extensions;
using HIM.Gateway.Models;
using HIM.Gateway.Models.Knowledge;
using HIM.Gateway.Services.SSH.Interfaces;
using HIM.Gateway.Services.SSH.Interfaces.ICommandDispatcher;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog.Context;
using Spectre.Console;

namespace HIM.Gateway.Services.SSH;

public class CommandService : ICommandService
{
    private readonly PortfolioData? _data;
    private readonly IAiClientService _aiClientService;
    private readonly ISlashCommandRegistry _commandRegistry;
    private readonly ICommandDispatcherHelper _commandDispatcherHelper;
    private readonly ITerminalLayoutService _terminalLayoutService;
    private readonly IThemeService _theme;
    private readonly ILogger<CommandService> _logger;
    private readonly UserSessionState _sessionState;
    private readonly int _maxAiQueriesPerSession;
    private readonly TimeSpan _cooldownDuration = TimeSpan.FromSeconds(3);

    public CommandService(
        IAiClientService aiClientService,
        ISlashCommandRegistry commandRegistry,
        ICommandDispatcherHelper commandDispatcherHelper,
        ITerminalLayoutService terminalLayoutService,
        IThemeService theme,
        ILogger<CommandService> logger,
        IPortfolioDataProvider portfolioDataProvider,
        UserSessionState sessionState,
        IOptions<SshSettings> sshSettings)
    {
        _aiClientService = aiClientService;
        _commandRegistry = commandRegistry;
        _commandDispatcherHelper = commandDispatcherHelper;
        _terminalLayoutService = terminalLayoutService;
        _theme = theme;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _sessionState = sessionState;
        _maxAiQueriesPerSession = sshSettings.Value.MaxAiQueriesPerSession;
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
            // Tokenize on the first word only, without RemoveEmptyEntries: today " /help"
            // (leading space) doesn't match any switch case and goes to the AI, and
            // RemoveEmptyEntries would silently start matching it - not a change worth making
            // inside a refactor.
            //
            // Task 19A: first-token matching also means every command now accepts trailing text
            // ("/menu extra" runs the menu), where the old whole-string switch sent it to the AI.
            // Accepted deliberately, not restricted to commands that declare they take arguments:
            // it is what let /theme stop needing a StartsWith special case, it matches how most
            // command-line tools behave, and the one real cost - "/help me understand his
            // Accenture work" now renders the table instead of reaching the AI - is rare enough
            // that it is better solved by the help text telling people to just type their
            // question than by a per-command AcceptsArguments flag.
            var token = command.Split(' ')[0];
            if (_commandRegistry.TryGet(token, out var slashCommand))
            {
                var context = new CommandContext(console, stream, command, _data, sessionId, ct);
                await slashCommand.ExecuteAsync(context);
                return;
            }

            if (IsRateLimited(_sessionState))
            {
                console.MarkupLine($"[yellow]![/] [grey]{Markup.Escape("Neural Link is cooling down.. please wait")}[/]");
                return;
            }

            // SEC-04: per-session query budget. Once hit, degrade gracefully to the
            // static commands instead of continuing to call the AI service - a visitor
            // still gets something useful, not an error.
            if (_sessionState.AiQueryCount >= _maxAiQueriesPerSession)
            {
                console.MarkupLine(
                    $"[yellow]![/] [grey]You've used up this session's {_maxAiQueriesPerSession} AI questions. " +
                    "Try [white]/menu[/] or [white]/stats[/] for what I already have on file.[/]");
                return;
            }
            _sessionState.AiQueryCount++;
            await HandleAiChatAsync(console, command, sessionId, ct);
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
                BorderStyle = new Style(_theme.PrimaryColor),
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
}