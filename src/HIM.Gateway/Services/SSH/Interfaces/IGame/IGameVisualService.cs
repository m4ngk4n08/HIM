using HIM.Gateway.Services.ServiceModel.Enums;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Text;

namespace HIM.Gateway.Services.SSH.Interfaces.IGame
{
    public interface IGameVisualService
    {

        Task ShowInstructionAsync(IAnsiConsole console, string gameName, string instructions, CancellationToken ct);

        /// <summary>
        /// Triggers a high-energy "Digital Surge" transition animation.
        /// </summary>
        /// <param name="console"></param>
        /// <param name="gameName"></param>
        /// <returns></returns>
        Task ShowTransitionAsync(IAnsiConsole console, string gameName);
        
        /// <summary>
        /// Renders a high-fidelity "Game Over" panel with rankings and high score status.
        /// </summary>
        /// <param name="console"></param>
        /// <param name="gameName"></param>
        /// <param name="currentScore"></param>
        /// <param name="maxScore"></param>
        /// <param name="reason"></param>
        /// <param name="score"></param>
        /// <param name="isNewHighScore"></param>
        void RenderGameOver(IAnsiConsole console, string gameName, int currentScore, int maxScore, string reason, bool isNewHighScore);

        /// <summary>
        /// Plays a standardized "Level up" animation or notification.
        /// </summary>
        /// <param name="console"></param>
        /// <param name="message"></param>
        /// <returns></returns>
        Task PlayLevelUpAnimationAsync(IAnsiConsole console, string message);

        /// <summary>
        /// Applies the engin'es consistent color palletes and theme to the console.
        /// </summary>
        /// <param name="console"></param>
        void ApplyGameTheme(IAnsiConsole console);
    }
}
