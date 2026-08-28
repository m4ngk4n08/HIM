using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Text;

namespace HIM.Gateway.Services.SSH.Interfaces.IGame
{
    public interface IGameService
    {
        string Name { get; }

        string Description { get; }

        /// <summary>
        /// Prepares the game for execution(e.g., loading assets, initializing state, setting up
        /// </summary>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task InitializeAsync(CancellationToken ct);

        /// <summary>
        /// Runs the main game loop
        /// </summary>
        /// <param name="console"></param>
        /// <param name="strea"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task ExecuteAsync(IAnsiConsole console, Stream strea, CancellationToken ct);

        /// <summary>
        /// Perform cleanup after the game loop terminates(e.g., saving state, releasing resources
        /// </summary>
        /// <returns></returns>
        Task ShutdownAsync();
    }
}
