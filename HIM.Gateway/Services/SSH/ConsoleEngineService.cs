using HIM.Gateway.Models;
using HIM.Gateway.Services.ServiceModel;
using HIM.Gateway.Services.SSH.Interfaces;
using Microsoft.Extensions.Options;
using Spectre.Console;
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
        private readonly ITerminalLayoutService _terminalLayoutService;

        public ConsoleEngineService(
            ICommandService commandService,
            ITerminalLayoutService terminalLayoutService,
            IOptions<SshSettings> settings)
        {
            _settings = settings.Value;
            _commandService = commandService;
            _terminalLayoutService = terminalLayoutService;
        }

        public IAnsiConsole CreateConsole(Stream stream, uint width, uint height)
        {
            var settings = new AnsiConsoleSettings
            {
                Ansi = AnsiSupport.Yes,
                ColorSystem = ColorSystemSupport.TrueColor,
                Out = new SshConsoleOutput(new SshTextWriter(stream, Encoding.UTF8), (int)width, (int)height), 
                Interactive = InteractionSupport.Yes
            };

            var console = AnsiConsole.Create(settings);

            console.Profile.Width = (int)(width > 0 ? width : DefaultWidth);
            console.Profile.Height = (int)(height > 0 ? height : DefaultHeight);
            
            console.Profile.Capabilities.Unicode = true;

            return console;
        }

        public async Task HandleInteractionLoopAsync(IAnsiConsole console, Stream stream, CancellationToken ct)
        {
            var commandHistory = new List<string>();
            int historyIndex = -1;
            int cursorPosition = 0;

            var inputBuffer = new StringBuilder();
            console.Write(new Text("> ", new Style(Color.Green)));

            byte[] buffer = new byte[1024];
            byte lastByte = 0;

            var idleTimeout = TimeSpan.FromMinutes(_settings.IdleTimeoutMinutes);

            // NOTE: We intentionally create a fresh linked CTS on every loop iteration.
            // CancelAfter() does NOT reset an existing timer — calling it again on the
            // same CTS only adds a second deadline; the first one still fires at the
            // original absolute time. Reusing a single CTS therefore causes the session
            // to be killed after IdleTimeoutMinutes from the *start* of the session
            // regardless of activity, producing "Cannot send more data after EOF".
            CancellationTokenSource? timeoutCts = null;

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    // Dispose the previous CTS and create a fresh one so the
                    // idle timer truly resets on every user keystroke.
                    timeoutCts?.Dispose();
                    timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeoutCts.CancelAfter(idleTimeout);
                    int read = await stream.ReadAsync(buffer, 0, buffer.Length, timeoutCts.Token);
                    if (read <= 0) break;

                    bool lineChanged = false;
                    bool commandExecuted = false;

                    for (int i = 0; i < read; i++)
                    {
                        byte b = buffer[i];

                        if(b == 10 && lastByte == 13)
                        {
                            lastByte = b;
                            continue;
                        }

                        if (b == 27 && i + 2 < read && buffer[i+1] == 91) // ESC [
                        {
                            byte key = buffer[i+2];
                            if (key == 65) // Up Arrow
                            {
                                if (commandHistory.Count > 0)
                                {
                                    if (historyIndex == -1) historyIndex = commandHistory.Count - 1;
                                    else if (historyIndex > 0) historyIndex--;
                                    else historyIndex = commandHistory.Count - 1;

                                    var cmd = commandHistory[historyIndex];
                                    inputBuffer.Clear();
                                    inputBuffer.Append(cmd);
                                    cursorPosition = cmd.Length;
                                    lineChanged = true;
                                }
                                i += 2;
                                continue;
                            }
                            else if (key == 66) // Down Arrow
                            {
                                if (historyIndex != -1)
                                {
                                    if (historyIndex < commandHistory.Count - 1) historyIndex++;
                                    else historyIndex = -1;

                                    string cmd = historyIndex == -1 ? "" : commandHistory[historyIndex];
                                    inputBuffer.Clear();
                                    inputBuffer.Append(cmd);
                                    cursorPosition = cmd.Length;
                                    lineChanged = true;
                                }
                                i += 2;
                                continue;
                            }
                            else if (key == 68) // Left Arrow
                            {
                                if (cursorPosition > 0)
                                {
                                    cursorPosition--;
                                    lineChanged = true;
                                }
                                i += 2;
                                continue;
                            }
                            else if (key == 67) // Right Arrow
                            {
                                if (cursorPosition < inputBuffer.Length)
                                {
                                    cursorPosition++;
                                    lineChanged = true;
                                }
                                i += 2;
                                continue;
                            }
                        }

                        if (b == 13 || b == 10)
                        {
                            var command = inputBuffer.ToString().Trim();
                            if (!string.IsNullOrWhiteSpace(command))
                            {
                                commandHistory.Add(command);
                            }
                            historyIndex = -1;
                            cursorPosition = 0;
                            inputBuffer.Clear();

                            console.WriteLine();
                            await _commandService.ProcessCommandAsync(console, command, stream, ct);
                            console.Write(new Text("> ", new Style(Color.Green)));
                            commandExecuted = true;
                        }
                        else if (b == 8 || b == 127)
                        {
                            if (cursorPosition > 0)
                            {
                                inputBuffer.Remove(cursorPosition - 1, 1);
                                cursorPosition--;
                                lineChanged = true;
                            }
                        }
                        else if (b >= 32 && b <= 126)
                        {
                            if (inputBuffer.Length < MaxInputLength)
                            {
                                inputBuffer.Insert(cursorPosition, (char)b);
                                cursorPosition++;
                                lineChanged = true;
                            }
                        }
                        lastByte = b;
                    }

                    if (lineChanged && !commandExecuted)
                    {
                        var cmdText = inputBuffer.ToString();
                        var sb = new StringBuilder();
                        
                        // \r: carriage return
                        // \x1b[K: clear line from cursor
                        // \x1b[32m> \x1b[0m: green prompt
                        sb.Append("\r\x1b[K\x1b[32m> \x1b[0m");
                        sb.Append(cmdText);

                        int moveBack = cmdText.Length - cursorPosition;
                        if (moveBack > 0)
                        {
                            sb.Append($"\x1b[{moveBack}D");
                        }

                        await stream.WriteAsync(Encoding.UTF8.GetBytes(sb.ToString()), ct);
                    }

                    await stream.FlushAsync(ct);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    console.MarkupLine("\n[red]Session timed out due to inactivity. Goodbye![/]");
                    await Task.Delay(1000, CancellationToken.None);
                    break;
                }
                catch (Exception ex) when (IsTransportException(ex))
                {
                    break;
                }
            }

            timeoutCts?.Dispose();
        }

        private static bool IsTransportException(Exception ex)
        {
            if (ex is AggregateException ae)
            {
                ex = ae.Flatten().InnerException ?? ex;
            }
            return ex is System.IO.IOException
                || ex is ObjectDisposedException
                || ex is InvalidOperationException
                || ex is OperationCanceledException;
        }

        public async Task RenderSplashScreenAsync(IAnsiConsole console, Stream stream, CancellationToken ct)
        {
            console.Write("\x1b]0;a11s.exe\x07");


            await console.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Initializing Neural Gateway...", async ctx =>
                {
                    await Task.Delay(500, ct);
                    ctx.Status("Retrieving Portfolio Knowledge Base...");
                    await Task.Delay(500, ct);
                    ctx.Status("Utilizing memory management...");
                    await Task.Delay(500, ct);
                    ctx.Status("Utilizing GC..");
                    await Task.Delay(500, ct);
                    ctx.Status("Settings up sandbox...");
                    await Task.Delay(500, ct);
                    ctx.Status("Retrieving Portfolio Knowledge Base...");
                    await Task.Delay(500, ct);
                    ctx.Status("Access Granted.");
                });

            await _terminalLayoutService.InitializeTerminalLayoutAsync(console, stream, ct);
        }
    }
}
