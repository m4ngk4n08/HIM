using HIM.Gateway.Services.SSH;
using HIM.Gateway.Services.SSH.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Text;

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

            return services;
        }
    }
}
