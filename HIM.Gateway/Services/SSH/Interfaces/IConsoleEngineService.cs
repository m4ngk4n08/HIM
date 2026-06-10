using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Text;

namespace HIM.Gateway.Services.SSH.Interfaces
{
    public interface IConsoleEngineService
    {
        IAnsiConsole CreateConsole(Stream stream, uint width, uint height);
        Task RenderSplashScreenAsync(IAnsiConsole console, CancellationToken ct);
        Task HandleInteractionLoopAsync(IAnsiConsole console, Stream stream, CancellationToken ct);
    }
}
