using HIM.Gateway.Services.SSH;
using HIM.Gateway.Services.SSH.CommandDispatcher;
using HIM.Gateway.Services.SSH.Game;
using HIM.Gateway.Services.SSH.Game.TheGame;
using HIM.Gateway.Services.SSH.Interfaces;
using HIM.Gateway.Services.SSH.Interfaces.ICommandDispatcher;
using HIM.Gateway.Services.SSH.Interfaces.IGame;
using Microsoft.Extensions.DependencyInjection;

namespace HIM.Gateway.Extensions
{
    public static class ServiceExtensions
    {
        public static IServiceCollection AddService(this IServiceCollection services)
        {
            // ── Singletons: process-wide state, safe to share across every visitor ──
            services.AddSingleton<IHostKeyService, HostKeyService>();
            services.AddSingleton<IAuthenticationService, GuestAuthenticationService>();
            services.AddSingleton<ISshServerListener, SshServerListener>();
            services.AddSingleton<IIpBanService, IpBanService>();

            // Persists high scores to a shared game-scores.json on disk - the leaderboard
            // is meant to be shared across every visitor, so Singleton is correct here.
            services.AddSingleton<IGameScoreService, GameScoreService>();

            // ── Scoped: one instance per SSH shell channel (session) ──
            // A scope is created in SshServerListener.HandleShellChannelAsync, around the
            // ITuiEngine.RunAsync call. Everything below is resolved from that scope, so
            // two concurrent sessions never share a TUI engine, command state, or game board.
            services.AddScoped<ITuiEngine, TuiEngine>();
            services.AddScoped<ICommandService, CommandService>();
            services.AddScoped<IConsoleEngineService, ConsoleEngineService>();
            services.AddScoped<UserSessionState>();

            services.AddScoped<IMenuCommandService, MenuCommandService>();
            services.AddScoped<IStatsCommandService, StatsCommandService>();
            services.AddScoped<IMatrixCommandService, MatrixCommandService>();
            services.AddScoped<IGameCommandService, GameCommandService>();
            services.AddScoped<ICommandDispatcherHelper, CommandDispatcherHelper>();

            // --- Layout Engine ---
            services.AddScoped<ITerminalLayoutService, TerminalLayoutService>();

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
    }
}
