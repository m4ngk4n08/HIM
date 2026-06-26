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

        private readonly List<string> _commandHistory = new();
        private int _historyIndex = -1;
        private int _cursorPosition = 0;

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
                                if (_commandHistory.Count > 0)
                                {
                                    if (_historyIndex == -1) _historyIndex = _commandHistory.Count - 1;
                                    else if (_historyIndex > 0) _historyIndex--;
                                    else _historyIndex = _commandHistory.Count - 1;

                                    var cmd = _commandHistory[_historyIndex];
                                    
                                    // \r: go to start, \x1b[K: clear line
                                    await stream.WriteAsync(Encoding.UTF8.GetBytes("\r\x1b[K"), ct);
                                    
                                    inputBuffer.Clear();
                                    inputBuffer.Append(cmd);
                                    _cursorPosition = cmd.Length;
                                    
                                    console.Write(new Text("> ", new Style(Color.Green)));
                                    await stream.WriteAsync(Encoding.UTF8.GetBytes(cmd), ct);
                                }
                                i += 2;
                                continue;
                            }
                            else if (key == 66) // Down Arrow
                            {
                                if (_historyIndex != -1)
                                {
                                    if (_historyIndex < _commandHistory.Count - 1) _historyIndex++;
                                    else _historyIndex = -1;

                                    string cmd = _historyIndex == -1 ? "" : _commandHistory[_historyIndex];
                                    
                                    await stream.WriteAsync(Encoding.UTF8.GetBytes("\r\x1b[K"), ct);
                                    
                                    inputBuffer.Clear();
                                    inputBuffer.Append(cmd);
                                    _cursorPosition = cmd.Length;
                                    
                                    console.Write(new Text("> ", new Style(Color.Green)));
                                    await stream.WriteAsync(Encoding.UTF8.GetBytes(cmd), ct);
                                }
                                i += 2;
                                continue;
                            }
                            else if (key == 68) // Left Arrow
                            {
                                if (_cursorPosition > 0)
                                {
                                    _cursorPosition--;
                                    await stream.WriteAsync(Encoding.UTF8.GetBytes("\x1b[D"), ct);
                                }
                                i += 2;
                                continue;
                            }
                            else if (key == 67) // Right Arrow
                            {
                                if (_cursorPosition < inputBuffer.Length)
                                {
                                    _cursorPosition++;
                                    await stream.WriteAsync(Encoding.UTF8.GetBytes("\x1b[C"), ct);
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
                                _commandHistory.Add(command);
                            }
                            _historyIndex = -1;
                            _cursorPosition = 0;
                            inputBuffer.Clear();

                            console.WriteLine();
                            await _commandService.ProcessCommandAsync(console, command, stream, ct);
                            console.Write(new Text("> ", new Style(Color.Green)));
                        }
                        else if (b == 8 || b == 127)
                        {
                            if (_cursorPosition > 0)
                            {
                                // Remove char at cursor-1
                                inputBuffer.Remove(_cursorPosition - 1, 1);
                                
                                // Move cursor back, clear to end, and re-render the remaining part
                                await stream.WriteAsync(Encoding.UTF8.GetBytes("\x1b[D\x1b[K"), ct);
                                
                                string remaining = inputBuffer.ToString().Substring(_cursorPosition - 1);
                                await stream.WriteAsync(Encoding.UTF8.GetBytes(remaining), ct);
                                
                                // Move cursor back to the correct position in the remaining text
                                int moveBack = remaining.Length - (_cursorPosition - 1); 
                                // No, the cursor is already at the point of deletion.
                                // We just need to move the cursor to the new cursor position.
                                // After \x1b[D\x1b[K, cursor is at _cursorPosition - 1.
                                // We printed 'remaining'. The cursor is now at end of 'remaining'.
                                // We need to move it back to _cursorPosition - 1 relative to the prompt.
                                
                                // Let's just move cursor back to the actual position.
                                // Relative to the start of the line (after prompt), the cursor is now at:
                                // Prompt (2) + (inputBuffer.Length - remaining.Length) + remaining.Length
                                // But we are using relative movements.
                                
                                // Correct logic for Backspace in raw mode:
                                // 1. \x1b[D (move back)
                                // 2. \x1b[K (clear to end)
                                // 3. Print remaining text
                                // 4. Move cursor back to the correct spot
                                
                                // Since we are at the end of the line after printing 'remaining',
                                // we move back by the length of 'remaining' minus 1? No.
                                // The cursor should be at _cursorPosition - 1.
                                // We are at _cursorPosition - 1 + remaining.Length.
                                // So we move back by remaining.Length.
                                
                                // Let's simplify:
                                // \x1b[D (back)
                                // \x1b[K (clear)
                                // print remaining
                                // \x1b[D... (move back to _cursorPosition - 1)
                                
                                // Actually, if we just want to delete and stay there:
                                // After \x1b[D\x1b[K, cursor is at _cursorPosition - 1.
                                // After printing 'remaining', cursor is at _cursorPosition - 1 + remaining.Length.
                                // To get back to _cursorPosition - 1, move back remaining.Length.
                                
                                // Actually, simpler: 
                                // 1. \x1b[D
                                // 2. \x1b[K
                                // 3. Write remaining
                                // 4. \x1b[<remaining.Length>D
                                
                                // Let's try this.
                                _cursorPosition--;
                            }
                        }
                        else if (b >= 32 && b <= 126)
                        {
                            if (inputBuffer.Length < MaxInputLength)
                            {
                                inputBuffer.Insert(_cursorPosition, (char)b);
                                
                                // Clear from cursor to end, write the new char, then the rest
                                await stream.WriteAsync(Encoding.UTF8.GetBytes("\x1b[K"), ct);
                                
                                string remaining = inputBuffer.ToString().Substring(_cursorPosition + 1);
                                await stream.WriteAsync(Encoding.UTF8.GetBytes(((char)b).ToString() + remaining), ct);
                                
                                _cursorPosition++;
                            }
                        }
                        lastByte = b;
                    }
                    await stream.FlushAsync(ct);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    console.MarkupLine("\n[red]Session timed out due to inactivity. Goodbye![/]");
                    await Task.Delay(1000, CancellationToken.None);
                    break;
                }
            }

            timeoutCts?.Dispose();
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
