using HIM.Gateway.Models;
using HIM.Gateway.Services.ServiceModel;
using HIM.Gateway.Services.SSH.Interfaces;
using Microsoft.Extensions.Options;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Runtime;
using System.Text;

namespace HIM.Gateway.Services.SSH
{
    public class ConsoleEngineService : IConsoleEngineService
    {
        private const uint DefaultWidth = 80;
        private const uint DefaultHeight = 24;
        private const int MaxInputLength = 256;
        private readonly SshSettings _settings;
        private readonly ICommandService _commandService;

        public ConsoleEngineService(
            ICommandService commandService,
            IOptions<SshSettings> settings)
        {
            _settings = settings.Value;
            _commandService = commandService;
        }

        public IAnsiConsole CreateConsole(Stream stream, uint width, uint height)
        {
            // Inject our custom SSH output bridge into Spectre's settings
            var settings = new AnsiConsoleSettings
            {
                Ansi = AnsiSupport.Yes,
                ColorSystem = ColorSystemSupport.TrueColor,
                Out = new SshConsoleOutput(new SshTextWriter(stream, Encoding.UTF8), (int)width, (int)height), 
                Interactive = InteractionSupport.Yes
            };

            var console = AnsiConsole.Create(settings);

            // Force absolute width/height detection
            console.Profile.Width = (int)(width > 0 ? width : DefaultWidth);
            console.Profile.Height = (int)(height > 0 ? height : DefaultHeight);
            console.Write($"width: {console.Profile.Width}, height: {console.Profile.Height}");
            
            // Critical for Alignment: Enable UTF8 for the profile
            console.Profile.Capabilities.Unicode = true;

            return console;
        }

        public async Task HandleInteractionLoopAsync(IAnsiConsole console, Stream stream, CancellationToken ct)
        {
            var inputBuffer = new StringBuilder();
            console.Write(new Text("> ", new Style(Color.Green)));

            byte[] buffer = new byte[1024];

            byte lastByte = 0;

            // --- SECURITY: Idle Timeout Watchdog ---
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var idleTimeout = TimeSpan.FromMinutes(_settings.IdleTimeoutMinutes);

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    // Reset timeout for each read attempt
                    timeoutCts.CancelAfter(idleTimeout);

                    // Await incoming network data with the watchdog token
                    int read = await stream.ReadAsync(buffer, 0, buffer.Length, timeoutCts.Token);
                    if (read <= 0) break;

                    for (int i = 0; i < read; i++)
                    {
                        byte b = buffer[i];

                        if(b == 10 && lastByte == 13)
                        {
                            lastByte = b;
                            continue;
                        }

                        // --- 1. Handle Enter Key (Command Execution) ---
                        if (b == 13 || b == 10)
                        {
                            // Extracts the accumulated characters as a string and trims leading/trailing whitespace.
                            var command = inputBuffer.ToString().Trim();

                            // Clears the buffer so the next command has clean starting state.
                            inputBuffer.Clear();


                            // EXECUTION FLOW CHANGE:
                            // We now pass the active Network/SSH stream to the Command Router.
                            // This provide a raw terminal control capabilities (VT100 escape codes) to sub-commands.
                            console.WriteLine();
                            await _commandService.ProcessCommandAsync(console, command, stream, ct);

                            console.Write(new Text("> ", new Style(Color.Green)));
                        }
                        // --- 2. Handle Backspace (Deletion) ---
                        else if (b == 8 || b == 127)
                        {
                            if (inputBuffer.Length > 0)
                            {
                                inputBuffer.Remove(inputBuffer.Length - 1, 1);
                                // Visual deletion: move cursor back, clear char, move back
                                await stream.WriteAsync(Encoding.UTF8.GetBytes("\b \b"), 0, 3, ct);
                            }
                        }
                        // --- 3. Handle Printable Characters (Echo) ---
                        else if (b >= 32 && b <= 126)
                        {
                            // SECURITY: Limit input length to prevent memory exhaustion
                            if (inputBuffer.Length < MaxInputLength)
                            {
                                inputBuffer.Append((char)b);
                                // Immediate network echo
                                await stream.WriteAsync(new[] { b }, 0, 1, ct);
                            }
                        }

                        lastByte = b;
                    }

                    await stream.FlushAsync(ct);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    // If the timeoutCts fired but the main ct is still active, it's an idle timeout
                    console.MarkupLine("\n[red]Session timed out due to inactivity. Goodbye![/]");
                    await Task.Delay(1000, CancellationToken.None); // Give time for the message to be sent
                    break;
                }
            }
        }

        public async Task RenderSplashScreenAsync(IAnsiConsole console, CancellationToken ct)
        {
            // Set the terminal window/tab title using ANSI OSC 0 sequence
            console.Write("\x1b]0;a11s.exe\x07");

            console.Clear();

            RenderHeader(console);

            console.Write(new Rule("[yellow]HEURISTIC INTERACTIVE MOCKUP[/]").Centered());
            console.WriteLine();

            // Simulated initialization for UX feedback
            await console.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Initializing Neural Gateway...", async ctx =>
                {
                    await Task.Delay(500, ct);
                    ctx.Status("Retrieving Portfolio Knowledge Base...");
                    await Task.Delay(500, ct);
                    ctx.Status("Access Granted.");
                });

            console.MarkupLine("[bold white]Welcome to Angelo's Portfolio.[/] [grey](SSH Edition)[/]");
            console.MarkupLine("[grey]Type [white]/help[/] for command list or start chatting with the AI.[/]");
            console.WriteLine();
        }

        private void RenderHeader(IAnsiConsole console)
        {
            if(console.Profile.Width >= 60)
            {
                // Render Aesthetic Header
                console.Write(
                    new FigletText("H I M")
                        .Centered()
                        .Color(Color.Cyan1));
            }
            else
            {
                console.Write(
                    new Text("--- H I M ---", new Style(Color.Cyan1, decoration: Decoration.Bold))
                    .Centered());
            }
        }
    }
}
