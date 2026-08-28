using HIM.Gateway.Services.ServiceModel.Enums;
using HIM.Gateway.Services.SSH.Interfaces.IGame;
using System;
using System.Collections.Generic;
using System.Text;

namespace HIM.Gateway.Services.SSH.Game
{
    /// <summary>
    /// High-performance implementation of <see cref="IGameInputService"/> designed for low latency
    /// terminal interaction. Handles ANSI escape sequence and converts them to <see cref="IGameInputService"/>
    /// </summary>
    internal sealed class GameInputService : IGameInputService
    {
        // Pre-allocated buffer to minimize GC pressure during high-frequency input loops.
        private readonly byte[] _buffer = new byte[16];

        /// <summary>
        /// Reads the next high-level input from the provided stream.
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        public async Task<GameInput> GetNextInputAsync(Stream stream, CancellationToken ct)
        {
            int bytesRead = await stream.ReadAsync(_buffer, 0, _buffer.Length, ct);

            if(bytesRead == 0)
            {
                return GameInput.None;
            }

            // Use a span for efficient, zero-allocation parsing of the read buffer.
            ReadOnlySpan<byte> inputSpan = _buffer.AsSpan(0, bytesRead);

            // 1. Handle ANSI Escape Sequences (e.g., Arrow keys)
            // Sequence pattern: ESC [ <char> (0x1B 0x5B <char>)
            if(bytesRead >= 3 && inputSpan[0] == 0x1B && inputSpan[1] == 0x5B)
            {
                return inputSpan[2] switch
                {
                    (byte)'A' => GameInput.Up,
                    (byte)'B' => GameInput.Down,
                    (byte)'C' => GameInput.Right,
                    (byte)'D' => GameInput.Left,
                    _ => GameInput.None
                };
            }

            return inputSpan[0] switch
            {
                (byte)'w' or (byte)'W' => GameInput.W,
                (byte)'a' or (byte)'A' => GameInput.A,
                (byte)'s' or (byte)'S' => GameInput.S,
                (byte)'d' or (byte)'D' => GameInput.D,
                (byte)' ' => GameInput.Space,
                (byte)'\r' or (byte)'\n' => GameInput.Enter,
                (byte)0x1B => GameInput.Escape, // Single ESC key press
                _ => GameInput.None
            };

        }
    }
}
