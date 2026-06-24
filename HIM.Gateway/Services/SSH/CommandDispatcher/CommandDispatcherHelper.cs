using HIM.Gateway.Services.SSH.Interfaces.ICommandDispatcher;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Text;

namespace HIM.Gateway.Services.SSH.CommandDispatcher
{
    internal sealed class CommandDispatcherHelper : ICommandDispatcherHelper
    {
        public CommandDispatcherHelper()
        {
            
        }
        public async Task<string> ReadInputManualAsync(IAnsiConsole console, Stream stream, CancellationToken ct)
        {
            var inputBuffer = new StringBuilder();
            byte[] buffer = new byte[1];

            while (!ct.IsCancellationRequested)
            {
                // Read a single byte from the SSH stream
                int read = await stream.ReadAsync(buffer, 0, 1, ct);
                if (read <= 0) break;

                byte b = buffer[0];

                // 13 = Carriage Return(enter)
                if (b == 13 || b == 10)
                {
                    console.WriteLine(); // Move to next line on enter
                    return inputBuffer.ToString().Trim();
                }

                // 8 or 127 = Backspace
                if (b == 8 || b == 127)
                {
                    if (inputBuffer.Length > 0)
                    {
                        inputBuffer.Remove(inputBuffer.Length - 1, 1);
                        // Send ANSI sequence to move cursor back, print space and move back again
                        console.Write("\b \b");
                    }
                    continue;
                }

                // Normal character: add to buffer and echo it back to the user
                char c = (char)b;
                inputBuffer.Append(c);
                console.Write(c.ToString());
            }

            return string.Empty;
        }
    }
}
