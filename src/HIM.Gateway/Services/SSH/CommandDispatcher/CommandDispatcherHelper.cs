using HIM.Gateway.Services.SSH.Interfaces;
using HIM.Gateway.Services.SSH.Interfaces.ICommandDispatcher;
using Spectre.Console;
using System.Text;

namespace HIM.Gateway.Services.SSH.CommandDispatcher
{
    internal sealed class CommandDispatcherHelper : ICommandDispatcherHelper
    {
        private readonly ISessionByteReader _byteReader;

        public CommandDispatcherHelper(ISessionByteReader byteReader)
        {
            _byteReader = byteReader;
        }

        public async Task<string> ReadInputManualAsync(IAnsiConsole console, Stream stream, CancellationToken ct)
        {
            var inputBuffer = new StringBuilder();
            byte[] buffer = new byte[1];

            while (!ct.IsCancellationRequested)
            {
                // Task 25: reads go through the shared session byte reader, not the stream
                // directly - it hands back whatever ConsoleEngineService's outer loop already
                // pulled off the wire but didn't consume (e.g. the rest of a pasted line) before
                // touching the socket itself.
                int read = await _byteReader.ReadAsync(stream, buffer, 0, 1, ct);
                if (read <= 0) break;

                byte b = buffer[0];

                if (b == 13 || b == 10)
                {
                    console.WriteLine();
                    return inputBuffer.ToString().Trim();
                }

                if (b == 8 || b == 127)
                {
                    if (inputBuffer.Length > 0)
                    {
                        inputBuffer.Remove(inputBuffer.Length - 1, 1);
                        console.Write("\b \b");
                    }
                    continue;
                }

                char c = (char)b;
                inputBuffer.Append(c);
                console.Write(c.ToString());
            }

            return string.Empty;
        }

        public async Task SetScrollingRegionAsync(Stream stream, int top, int bottom, CancellationToken ct)
        {
            // ANSI DECSTBM: ESC [ <top> ; <bottom> r
            var sequence = $"\x1b[{top};{bottom}r";
            await stream.WriteAsync(Encoding.UTF8.GetBytes(sequence), ct);
        }

        public async Task ResetScrollingRegionAsync(Stream stream, CancellationToken ct)
        {
            // ANSI DECSTBM with no parameters restores the region to the full screen. DECSTBM
            // persists in the client's own terminal after the session ends, so this must run on
            // every exit path - otherwise a visitor is left with a broken scroll region after
            // disconnecting.
            var sequence = "\x1b[r";
            await stream.WriteAsync(Encoding.UTF8.GetBytes(sequence), ct);
        }

        public async Task MoveCursorAsync(Stream stream, int row, int col, CancellationToken ct)
        {
            // ANSI CUP: ESC [ <row> ; <col> H
            var sequence = $"\x1b[{row};{col}H";
            await stream.WriteAsync(Encoding.UTF8.GetBytes(sequence), ct);
        }
    }
}
