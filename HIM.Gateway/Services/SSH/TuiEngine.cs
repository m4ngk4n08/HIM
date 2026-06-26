using HIM.Gateway.Models;
using HIM.Gateway.Services.ServiceModel;
using HIM.Gateway.Services.SSH.Interfaces;
using Microsoft.DevTunnels.Ssh;
using Microsoft.Extensions.Options;
using Spectre.Console;
using System.Collections.Concurrent;
using System.Text;

namespace HIM.Gateway.Services.SSH
{
    /// <summary>
    /// A robust TUI Engine providing a sandboxed, interactive terminal experience.
    /// Manages the lifecycle of an SSH session, including ANSI rendering and input processing.
    /// </summary>
    public class TuiEngine : ITuiEngine
    {
        private readonly IConsoleEngineService _consoleEngineService;
        private readonly ConcurrentDictionary<SshChannel, IAnsiConsole> _activeConsoles = new();
        public TuiEngine(IConsoleEngineService consoleEngineService)
        {
            _consoleEngineService = consoleEngineService;
        }

        public void HandleResize(SshChannel channel, uint width, uint height)
        {
            if(_activeConsoles.TryGetValue(channel, out var console))
            {
                // Update the Specter.Console profile dynamically
                console.Profile.Width = (int)width;
                console.Profile.Height = (int)height;

                console.Clear();
                // Inform the user or redraw the prompt
                console.MarkupLine("[grey](Terminal resized to {0}x{1})[/]", width, height);
                console.Write(new Text("> ", new Style(Color.Green)));
            }
        }

        public async Task RunAsync(SshChannel channel, uint width, uint height, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(channel);

            try
            {
                // 1. Establish the bidirectional network stream over the SSH channel
                using var sshStream = new SshStream(channel);

                // 2. Initialize Spectre.Console with a custom output bridge to the SSH stream
                var console = _consoleEngineService.CreateConsole(sshStream, width, height);

                // 3. Execute the Visual Initialization (Splash Screen)
                await _consoleEngineService.RenderSplashScreenAsync(console, sshStream, ct);

                // 4. Start the Interactive Command & AI Chat Loop
                await _consoleEngineService.HandleInteractionLoopAsync(console, sshStream, ct);

                sshStream.Dispose();
            }
            catch (OperationCanceledException)
            {
                // Expected teardown on client disconnect or server shutdown
                Console.WriteLine($"[TUI] Clean exit for channel {channel.ChannelId}.");
            }
            catch (Exception ex)
            {
                // Error boundary to protect the Gateway process
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[TUI Error] Fatal exception in session {channel.ChannelId}: {ex.Message}");
                Console.ResetColor();
            }
            finally
            {
                _activeConsoles.TryRemove(channel, out _);
            }
        }

    }

}
