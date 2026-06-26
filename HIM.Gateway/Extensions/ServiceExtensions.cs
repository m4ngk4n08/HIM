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

            // Register infrastructure services as Singletons to maintain state (keys/auth/listener)
            services.AddSingleton<IHostKeyService, HostKeyService>();
            services.AddSingleton<IAuthenticationService, GuestAuthenticationService>();
            services.AddSingleton<ISshServerListener, SshServerListener>();
            services.AddSingleton<ITuiEngine, TuiEngine>();
            services.AddSingleton<ICommandService, CommandService>();
            services.AddSingleton<IConsoleEngineService, ConsoleEngineService>();
            services.AddSingleton<IIpBanService, IpBanService>();

            services.AddSingleton<IMenuCommandService, MenuCommandService>();
            services.AddSingleton<IStatsCommandService, StatsCommandService>();
            services.AddSingleton<IMatrixCommandService, MatrixCommandService>();
            services.AddSingleton<IGameCommandService, GameCommandService>();
            services.AddSingleton<ICommandDispatcherHelper, CommandDispatcherHelper>();

            // --- Layout Engine ---
            services.AddSingleton<ITerminalLayoutService, TerminalLayoutService>();

            // --- Game Engine ---
            // The Factory resolves the games
            services.AddSingleton<IGameFactoryService, GameFactoryService>();

            // Core engine services
            services.AddSingleton<IGameInputService, GameInputService>();
            services.AddSingleton<IGameScoreService, GameScoreService>();
            services.AddSingleton<IGameVisualService, GameVisualService>();

            // Register individual game implementations
            // The GameFactoryService will automatically pick these up via IEnumerable<IGameService>
            services.AddSingleton<IGameService, TriviaGame>();
            services.AddSingleton<IGameService, RegexQuest>();
            services.AddSingleton<IGameService, CodeDebugger>();
            services.AddSingleton<IGameService, PacManGame>();
            return services;
        }
    }
}
