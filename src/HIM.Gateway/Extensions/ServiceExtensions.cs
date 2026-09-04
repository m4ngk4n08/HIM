using HIM.Gateway.Services.SSH;
using HIM.Gateway.Services.SSH.CommandDispatcher;
using HIM.Gateway.Services.SSH.Commands;
using HIM.Gateway.Services.SSH.Gates;
using HIM.Gateway.Services.SSH.Game;
using HIM.Gateway.Services.SSH.Game.TheGame;
using HIM.Gateway.Services.SSH.Interfaces;
using HIM.Gateway.Services.SSH.Interfaces.ICommandDispatcher;
using HIM.Gateway.Services.SSH.Interfaces.IGame;
using HIM.Gateway.Services.SSH.Interfaces.IGates;
using Microsoft.Extensions.DependencyInjection;

namespace HIM.Gateway.Extensions
{
    public static class ServiceExtensions
    {
        // Enforce correct service lifetimes at startup: ValidateScopes catches a scoped service
        // resolved from the root provider (which would silently pin one visitor's session state
        // for the life of the process), and ValidateOnBuild catches a singleton that captures a
        // scoped/transient dependency in its constructor. Both fail fast instead of failing silently.
        // Values shared by Program.cs and the test service provider factory so they can't drift apart.
        public static ServiceProviderOptions ContainerValidationOptions => new()
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        };

        public static IServiceCollection AddService(this IServiceCollection services)
        {
            // ── Singletons: process-wide state, safe to share across every visitor ──
            services.AddSingleton(TimeProvider.System);
            services.AddSingleton<IHostKeyService, HostKeyService>();
            services.AddSingleton<IAuthenticationService, GuestAuthenticationService>();
            services.AddSingleton<ISshServerListener, SshServerListener>();
            services.AddSingleton<IIpBanService, IpBanService>();

            // ── Connection gates: registration order is evaluation order ──
            services.AddConnectionGate<GlobalFloodGate>();
            services.AddConnectionGate<IpBanGate>();
            services.AddConnectionGate<PerIpRateGate>();
            services.AddConnectionGate<PerIpConcurrencyGate>();
            services.AddSingleton<IConnectionSlotGate>(sp => sp.GetRequiredService<PerIpConcurrencyGate>());

            // Task 23C: per-layer accept/reject counters for /defense, filled in from
            // SshServerListener.EvaluateGates - resolved after the four gates above so its
            // constructor sees the real IEnumerable<IConnectionGate> in registration order.
            services.AddSingleton<IConnectionMetricsService, ConnectionMetricsService>();

            // Task 24C: who's connected right now, for /who. Registered/deregistered by
            // SshServerListener around each session's DI scope - one process-wide table, so
            // Singleton is correct, matching IConnectionMetricsService and IIpBanService above.
            services.AddSingleton<ISessionRegistryService, SessionRegistryService>();

            // Persists high scores to a shared game-scores.json on disk - the leaderboard
            // is meant to be shared across every visitor, so Singleton is correct here.
            services.AddSingleton<IGameScoreService, GameScoreService>();

            // The knowledge base is immutable and identical for every session - load it once
            // and hand out the parsed object, instead of re-reading it per connection.
            services.AddSingleton<IPortfolioDataProvider, PortfolioDataProvider>();

            // Slash command discovery: reflect over the assembly once at startup (an SSH
            // connection gets its own DI scope, so scanning here instead of in the registry
            // means this runs once per process, not once per visitor). A duplicate [SlashCommand]
            // name throws here, at startup, not at first use.
            var slashCommandCatalog = SlashCommandCatalog.Discover(typeof(ServiceExtensions).Assembly);
            services.AddSingleton(slashCommandCatalog);
            services.AddScoped<ISlashCommandRegistry, SlashCommandRegistry>();

            // Registered FROM the catalog rather than by hand. Discovery is automatic, so
            // registration has to be too: a hand-written list means a new [SlashCommand] class is
            // discovered, listed by /help, and then throws GetRequiredService when a visitor types
            // it - /help advertising a command that kills the session is the exact drift Move 3
            // exists to prevent, just moved from the help table into the container. Scoped for the
            // same reason as the registry: handlers wrap scoped I*CommandService instances.
            foreach (var descriptor in slashCommandCatalog.Descriptors)
                services.AddScoped(descriptor.HandlerType);

            // ── Scoped: one instance per SSH shell channel (session) ──
            // A scope is created in SshServerListener.HandleShellChannelAsync, around the
            // ITuiEngine.RunAsync call. Everything below is resolved from that scope, so
            // two concurrent sessions never share a TUI engine, command state, or game board.
            services.AddScoped<ITuiEngine, TuiEngine>();
            services.AddScoped<ICommandService, CommandService>();
            services.AddScoped<IConsoleEngineService, ConsoleEngineService>();
            services.AddScoped<UserSessionState>();

            // Task 26B: /tour's selected mode, current step and active flag - one instance per
            // session, exactly like UserSessionState above and for the same reason.
            services.AddScoped<TourState>();

            // Task 25: one owner for the session's stream reads, shared by ConsoleEngineService's
            // outer loop and CommandDispatcherHelper's nested prompt reader. Scoped, not
            // Singleton - it holds one visitor's unconsumed bytes.
            services.AddScoped<ISessionByteReader, SessionByteReader>();

            services.AddScoped<IMenuCommandService, MenuCommandService>();
            services.AddScoped<IStatsCommandService, StatsCommandService>();
            services.AddScoped<IMatrixCommandService, MatrixCommandService>();
            services.AddScoped<IGameCommandService, GameCommandService>();
            services.AddScoped<ICommandDispatcherHelper, CommandDispatcherHelper>();

            // --- Layout Engine ---
            services.AddScoped<ITerminalLayoutService, TerminalLayoutService>();
            services.AddScoped<IThemeService, ThemeService>();

            // --- Game Engine ---
            // The Factory resolves the games
            services.AddScoped<IGameFactoryService, GameFactoryService>();

            // Core engine services
            services.AddScoped<IGameInputService, GameInputService>();
            services.AddScoped<IGameVisualService, GameVisualService>();

            // Register individual game implementations
            // The GameFactoryService will automatically pick these up via IEnumerable<IGameService>
            services.AddScoped<IGameService, TriviaGame>();
            services.AddScoped<IGameService, RegexQuest>();
            services.AddScoped<IGameService, CodeDebugger>();
            services.AddScoped<IGameService, PacManGame>();
            return services;
        }

        /// <summary>
        /// Registers a connection gate both under its concrete type and as an IConnectionGate,
        /// resolving to the same singleton instance either way. Registration order is evaluation
        /// order — IEnumerable&lt;IConnectionGate&gt; preserves the order services were added in
        /// Microsoft.Extensions.DependencyInjection, pinned by
        /// ConnectionGatePipelineTests.RegistrationOrder_IsEvaluationOrder.
        /// </summary>
        public static IServiceCollection AddConnectionGate<TGate>(this IServiceCollection services)
            where TGate : class, IConnectionGate
        {
            services.AddSingleton<TGate>();
            services.AddSingleton<IConnectionGate>(sp => sp.GetRequiredService<TGate>());
            return services;
        }
    }
}
