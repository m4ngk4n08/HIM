using HIM.Gateway.Services.SSH;
using HIM.Gateway.Services.SSH.CommandDispatcher;
using HIM.Gateway.Services.SSH.Interfaces;
using HIM.Gateway.Services.SSH.Interfaces.ICommandDispatcher;
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

            services.AddSingleton<IMenuCommandService, MenuCommandService>();
            services.AddSingleton<IStatsCommandService, StatsCommandService>();
            services.AddSingleton<IMatrixCommandService, MatrixCommandService>();
            services.AddSingleton<IGameCommandService, GameCommandService>();

            return services;
        }
    }
}
