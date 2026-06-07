using HIM.Gateway.Models;
using HIM.Gateway.Services.SSH.Interfaces;
using Microsoft.DevTunnels.Ssh;
using Spectre.Console;
using System.Text;

namespace HIM.Gateway.Services.SSH
{
    /// <summary>
    /// A robust TUI Engine providing a sandboxed, interactive terminal experience.
    /// Manages the lifecycle of an SSH session, including ANSI rendering and input processing.
    /// </summary>
    public class TuiEngine : ITuiEngine
    {
        private const int DefaultWidth = 80;
        private const int DefaultHeight = 24;
        private SshSettings sshSettings;

        public async Task RunAsync(SshChannel channel, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(channel);

            try
            {
                // 1. Establish the bidirectional network stream over the SSH channel
                using var sshStream = new SshStream(channel);

                // 2. Initialize Spectre.Console with a custom output bridge to the SSH stream
                var console = CreateConsole(sshStream);

                // 3. Execute the Visual Initialization (Splash Screen)
                await RenderSplashScreenAsync(console, ct);

                // 4. Start the Interactive Command & AI Chat Loop
                await HandleInteractionLoopAsync(console, sshStream, ct);
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
        }

        private IAnsiConsole CreateConsole(Stream stream)
        {
            // Inject our custom SSH output bridge into Spectre's settings
            var settings = new AnsiConsoleSettings
            {
                Ansi = AnsiSupport.Yes,
                ColorSystem = ColorSystemSupport.TrueColor,
                Out = new SshConsoleOutput(new SshTextWriter(stream)),
                Interactive = InteractionSupport.Yes
            };

            var console = AnsiConsole.Create(settings);

            // Synchronize the Spectre Profile with the terminal dimensions
            console.Profile.Width = DefaultWidth;
            console.Profile.Height = DefaultHeight;

            return console;
        }

        private async Task RenderSplashScreenAsync(IAnsiConsole console, CancellationToken ct)
        {
            console.Clear();

            // Render Aesthetic Header
            console.Write(
                new FigletText("H I M")
                    .Centered()
                    .Color(Color.Cyan1));

            console.Write(new Rule("[yellow]HEURISTIC INTERACTIVE MOCKUP[/]").Centered());
            console.WriteLine();

            // Simulated initialization for UX feedback
            await console.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Initializing Neural Gateway...", async ctx =>
                {
                    await Task.Delay(2000, ct);
                    ctx.Status("Retrieving Portfolio Knowledge Base...");
                    await Task.Delay(1000, ct);
                    ctx.Status("Access Granted.");
                });

            console.MarkupLine("[bold white]Welcome to Angelo's Portfolio.[/] [grey](SSH Edition)[/]");
            console.MarkupLine("[grey]Type [white]/help[/] for command list or start chatting with the AI.[/]");
            console.WriteLine();
        }

        private async Task HandleInteractionLoopAsync(IAnsiConsole console, Stream stream, CancellationToken ct)
        {
            var inputBuffer = new StringBuilder();
            console.Write(new Text("> ", new Style(Color.Green)));

            byte[] buffer = new byte[1024];

            while (!ct.IsCancellationRequested)
            {
                // Await incoming network data
                int read = await stream.ReadAsync(buffer, 0, buffer.Length, ct);
                if (read <= 0) break;

                for (int i = 0; i < read; i++)
                {
                    byte b = buffer[i];

                    // --- 1. Handle Enter Key (Command Execution) ---
                    if (b == 13 || b == 10)
                    {
                        var command = inputBuffer.ToString().Trim();
                        inputBuffer.Clear();

                        console.WriteLine();
                        await ProcessCommandAsync(console, command, ct);
                        
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
                        inputBuffer.Append((char)b);
                        // Immediate network echo
                        await stream.WriteAsync(new[] { b }, 0, 1, ct);
                    }
                }
                
                await stream.FlushAsync(ct);
            }
        }

        private async Task ProcessCommandAsync(IAnsiConsole console, string command, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(command)) return;

            switch (command.ToLower())
            {
                case "/help":
                    var table = new Table().Border(TableBorder.Rounded).Expand();
                    table.AddColumn("[yellow]Command[/]");
                    table.AddColumn("[yellow]Description[/]");
                    table.AddRow("/about", "Who is Angelo?");
                    table.AddRow("/projects", "Display list of technical projects.");
                    table.AddRow("/clear", "Reset the terminal view.");
                    table.AddRow("/exit", "Terminate the SSH connection.");
                    console.Write(table);
                    break;

                case "/clear":
                    console.Clear();
                    break;

                case "/exit":
                    console.MarkupLine("[red]Closing connection...[/]");
                    throw new OperationCanceledException();

                default:
                    console.MarkupLine($"[cyan1]AI:[/] That's a great question about [italic]{Markup.Escape(command)}[/].");
                    console.MarkupLine("[grey](RAG integration with Llama3 is the next milestone!)[/]");
                    break;
            }
        }
    }

    #region Infrastructure: SSH-to-Spectre Bridge

    /// <summary>
    /// Bridges Spectre's IAnsiConsoleOutput interface to our SSH-specific stream writer.
    /// </summary>
    internal class SshConsoleOutput : IAnsiConsoleOutput
    {
        private readonly SshTextWriter _sshWriter;
        public TextWriter Writer => _sshWriter;
        public bool IsTerminal => true;
        public int Width => 80;
        public int Height => 24;

        public SshConsoleOutput(SshTextWriter writer) => _sshWriter = writer;

        /// <summary>
        /// Complies with the IAnsiConsoleOutput contract to sync character encoding.
        /// </summary>
        public void SetEncoding(Encoding encoding) => _sshWriter.ApplyEncoding(encoding);

        public void SetRawMode(bool enable) { }
    }

    /// <summary>
    /// A custom TextWriter that wraps an SSH network stream, ensuring immediate flushing
    /// and correct character encoding for terminal rendering.
    /// </summary>
    internal class SshTextWriter : TextWriter
    {
        private readonly Stream _stream;
        private Encoding _encoding = Encoding.UTF8;

        public override Encoding Encoding => _encoding;

        public SshTextWriter(Stream stream) => _stream = stream;

        public void ApplyEncoding(Encoding encoding) => _encoding = encoding ?? Encoding.UTF8;

        public override void Write(string? value)
        {
            if (string.IsNullOrEmpty(value)) return;
            var bytes = _encoding.GetBytes(value);
            _stream.Write(bytes, 0, bytes.Length);
            _stream.Flush();
        }

        public override void Write(char value) => Write(value.ToString());
    }

    #endregion
}
