using HIM.Gateway.Extensions;
using HIM.Gateway.Models;
using HIM.Gateway.Services.SSH.Interfaces;
using Spectre.Console;
using System;
using System.Linq;

namespace HIM.Gateway.Services.SSH.Commands
{
    // Task 24C, Phase 4 module "who" from the rebuild artifact. Shows who else is connected right
    // now: session id, masked IP and connection duration only - never the SSH username, which is
    // attacker-controlled network input SanitizeLogInput was built to sanitize for a log line, not
    // for another visitor's terminal (see plans/sonnet-task-24-loose-ends-and-who.md).
    [SlashCommand("/who", "See who else is connected right now", HelpOrder = 10)]
    public sealed class WhoCommand : ISlashCommand
    {
        private readonly ISessionRegistryService _registry;
        private readonly IThemeService _theme;
        private readonly TimeProvider _timeProvider;

        public WhoCommand(ISessionRegistryService registry, IThemeService theme, TimeProvider timeProvider)
        {
            _registry = registry;
            _theme = theme;
            _timeProvider = timeProvider;
        }

        public Task ExecuteAsync(CommandContext context)
        {
            var console = context.Console;
            var sessions = _registry.GetActiveSessions();
            var now = _timeProvider.GetUtcNow().UtcDateTime;

            console.Write(new Rule("[bold]WHO'S HERE[/]").RuleStyle(_theme.PrimaryColor));
            console.MarkupLine(sessions.Count == 1
                ? "[grey]Just you right now.[/]"
                : $"[grey]{sessions.Count} people connected right now.[/]");

            var table = new Table().Border(TableBorder.Rounded).Title("[bold]SESSIONS[/]");
            table.AddColumn("Session").AddColumn("IP (network only)").AddColumn("Connected for");

            foreach (var session in sessions.OrderBy(s => s.ConnectedAtUtc))
            {
                var isYou = session.SessionId == context.SessionId;
                var label = SanitizerExtension.RedactPhone(ShortId(session.SessionId)).EscapeMarkup();
                if (isYou) label += " [cyan1](you)[/]";

                var maskedIp = SanitizerExtension.RedactPhone(IpMaskExtension.MaskIp(session.IpAddress)).EscapeMarkup();
                var duration = FormatDuration(now - session.ConnectedAtUtc);

                table.AddRow(label, maskedIp, duration);
            }

            console.Write(table);
            return Task.CompletedTask;
        }

        // The full GUID is 36 characters of noise nobody needs to read at a glance; the first
        // 8 are plenty to tell two concurrent visitors' rows apart.
        private static string ShortId(string sessionId) =>
            sessionId.Length > 8 ? sessionId[..8] : sessionId;

        private static string FormatDuration(TimeSpan duration) =>
            duration.TotalHours >= 1
                ? $"{(int)duration.TotalHours}h {duration.Minutes}m"
                : duration.TotalMinutes >= 1
                    ? $"{(int)duration.TotalMinutes}m {duration.Seconds}s"
                    : $"{Math.Max(duration.Seconds, 0)}s";
    }
}
