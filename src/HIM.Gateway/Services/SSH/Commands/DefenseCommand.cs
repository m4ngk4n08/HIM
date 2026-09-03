using HIM.Gateway.Extensions;
using HIM.Gateway.Models;
using HIM.Gateway.Services.SSH.Gates;
using HIM.Gateway.Services.SSH.Interfaces;
using Microsoft.Extensions.Options;
using Spectre.Console;

namespace HIM.Gateway.Services.SSH.Commands
{
    // Task 23C, Phase 4 module "stats --live" from the rebuild artifact. Built as its own
    // registry command rather than a /stats flag: /stats is the portfolio skills card
    // (StatsCommandService) and has nothing to do with the defense pipeline, and this TUI has no
    // flag grammar to begin with (the same reasoning Task 22C used for /cite).
    [SlashCommand("/defense", "Live view of the 8-layer connection defense pipeline", HelpOrder = 9)]
    public sealed class DefenseCommand : ISlashCommand
    {
        private readonly IConnectionMetricsService _metrics;
        private readonly IIpBanService _ipBanService;
        private readonly PerIpRateGate _rateGate;
        private readonly PerIpConcurrencyGate _concurrencyGate;
        private readonly IThemeService _theme;
        private readonly SshSettings _settings;

        public DefenseCommand(
            IConnectionMetricsService metrics,
            IIpBanService ipBanService,
            PerIpRateGate rateGate,
            PerIpConcurrencyGate concurrencyGate,
            IThemeService theme,
            IOptions<SshSettings> settings)
        {
            _metrics = metrics;
            _ipBanService = ipBanService;
            _rateGate = rateGate;
            _concurrencyGate = concurrencyGate;
            _theme = theme;
            _settings = settings.Value;
        }

        public Task ExecuteAsync(CommandContext context)
        {
            var console = context.Console;
            var snapshot = _metrics.GetSnapshot();

            console.Write(new Rule("[bold]DEFENSE PIPELINE[/]").RuleStyle(_theme.PrimaryColor));
            console.MarkupLine(
                $"[grey]Up for {FormatUptime(snapshot.Uptime)} — {snapshot.TotalEvaluated} connections seen, " +
                $"{snapshot.TotalAllowed} let in, {snapshot.TotalRejected} turned away.[/]");

            var layerTable = new Table().Border(TableBorder.Rounded).Title("[bold]WHAT EACH LAYER CAUGHT[/]");
            layerTable.AddColumn("Layer").AddColumn("Turned away").AddColumn("What that means");

            foreach (var (layer, rejected) in snapshot.RejectionsPerLayer)
            {
                var safeLayer = SanitizerExtension.RedactPhone(layer).EscapeMarkup();
                layerTable.AddRow(safeLayer, rejected.ToString(), Explain(layer, rejected));
            }

            console.Write(layerTable);

            var bans = _ipBanService.GetActiveBans();
            if (bans.Count == 0)
            {
                console.MarkupLine("[grey]No IPs are currently banned.[/]");
            }
            else
            {
                var banTable = new Table().Border(TableBorder.Rounded).Title("[bold]CURRENTLY BANNED[/]");
                banTable.AddColumn("IP (network only)").AddColumn("Strikes").AddColumn("Banned until (UTC)");

                foreach (var ban in bans)
                {
                    // Two different jobs, deliberately not merged: IpMaskExtension hides which
                    // host the address belongs to; RedactPhone is the free-text egress boundary
                    // every rendered string here goes through regardless of content.
                    var maskedIp = SanitizerExtension.RedactPhone(IpMaskExtension.MaskIp(ban.IpAddress)).EscapeMarkup();
                    banTable.AddRow(maskedIp, ban.StrikeCount.ToString(), ban.BanExpiresUtc.ToString("yyyy-MM-dd HH:mm:ss"));
                }

                console.Write(banTable);
            }

            console.MarkupLine(
                $"[grey]Currently watching {_rateGate.TrackedIpCount} IP(s) for rate limiting and " +
                $"{_concurrencyGate.TrackedIpCount} IP(s) for concurrent connections.[/]");

            return Task.CompletedTask;
        }

        private static string FormatUptime(TimeSpan uptime) =>
            uptime.TotalDays >= 1
                ? $"{(int)uptime.TotalDays}d {uptime.Hours}h {uptime.Minutes}m"
                : uptime.TotalHours >= 1
                    ? $"{(int)uptime.TotalHours}h {uptime.Minutes}m"
                    : $"{uptime.Minutes}m {uptime.Seconds}s";

        // Plain-English gloss for each defense layer, keyed off the exact Layer strings the gate
        // classes already expose (and ConnectionGatePipelineTests already pins) - a layer this
        // command has never heard of still renders a generic sentence instead of throwing.
        private string Explain(string layer, long rejected) => layer switch
        {
            "L3 GlobalFlood" =>
                $"[grey]arrived faster than {_settings.MaxGlobalConnectionsPerSecond} connections/second across the whole site.[/]",
            "L1 IpBan" =>
                "[grey]came from an IP already banned for past abuse.[/]",
            "L4 PerIpRate" =>
                $"[grey]came from one IP faster than {_settings.RateLimitMaxAttempts} attempts per {_settings.RateLimitWindowSeconds}s.[/]",
            "L5 PerIpConcurrency" =>
                $"[grey]would have given one IP more than {_settings.MaxConcurrentPerIp} connections at once.[/]",
            _ => "[grey]did not clear this layer's check.[/]"
        };
    }
}
