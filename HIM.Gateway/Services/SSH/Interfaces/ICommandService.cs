using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Text;

namespace HIM.Gateway.Services.SSH.Interfaces
{
    public interface ICommandService
    {
        /// <summary>
        /// Process a command using only the Spectre.Console inteface <- this acts our sandbox.
        /// </summary>
        /// <param name="console"></param>
        /// <param name="question"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        //Task ProcessCommandAsync(IAnsiConsole console, string question, CancellationToken ct);

        /// <summary>
        /// Stream-aware overload for commands requiring direct ANSI cursor control (e.g. /matrix).
        /// </summary>
        /// <param name="console"></param>
        /// <param name="command"></param>
        /// <param name="stream"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task ProcessCommandAsync(IAnsiConsole console, string command, Stream stream, CancellationToken ct);
    }
}
