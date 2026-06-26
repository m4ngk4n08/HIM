using HIM.Gateway.Services.SSH.Interfaces.ICommandDispatcher;
using Spectre.Console;
using System.Text;

namespace HIM.Gateway.Services.SSH.CommandDispatcher
{
    internal sealed class CommandDispatcherHelper : ICommandDispatcherHelper
    {
        public async Task<string> ReadInputManualAsync(IAnsiConsole console, Stream stream, CancellationToken ct)
        {
            var inputBuffer = new StringBuilder();
            byte[] buffer = new byte[1];

            while (!ct.IsCancellationRequested)
            {
                int read = await stream.ReadAsync(buffer, 0, 1, ct);
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

        public async Task MoveCursorAsync(Stream stream, int row, int col, CancellationToken ct)
        {
            // ANSI CUP: ESC [ <row> ; <col> H
            var sequence = $"\x1b[{row};{col}H";
            await stream.WriteAsync(Encoding.UTF8.GetBytes(sequence), ct);
        }
    }
}
