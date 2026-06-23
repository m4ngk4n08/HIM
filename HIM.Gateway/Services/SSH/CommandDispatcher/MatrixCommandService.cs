using HIM.Gateway.Services.SSH.Interfaces.ICommandDispatcher;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Text;

namespace HIM.Gateway.Services.SSH.CommandDispatcher
{
    internal sealed class MatrixCommandService : IMatrixCommandService
    {
        private static readonly char[] GlyphSet =
            "abcdefghijklmnopqrstuvwxyz0123456789@#$%&*<>".ToCharArray();

        private const int FrameDelayMs = 80;
        private const int MaxDurationMs = 15_000;

        public async Task ExecuteAsync(IAnsiConsole console, Stream stream, CancellationToken ct)
        {
            if (!stream.CanWrite)
            {
                console.MarkupLine("[red]Matrix animation requires a writable SSH stream.[/]");
                return;
            }

            var width = Math.Max(console.Profile.Width, 20);
            var height = Math.Max(console.Profile.Height - 2, 10);
            var encoding = Encoding.UTF8;
            var rng = new Random();

            var columns = new int[width];
            var speeds = new int[width];
            var ticks = new int[width];

            for (int c = 0; c < width; c++)
            {
                columns[c] = rng.Next(0, height);
                speeds[c] = rng.Next(1, 4);
            }

            using var animCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            animCts.CancelAfter(MaxDurationMs);

            await WriteRawAsync(stream, encoding, "\x1b[?25l\x1b[2J", ct);
            await WriteRawAsync(stream, encoding,
                $"\x1b[{height + 1};1H\x1b[0m\x1b[2m  [Animation: 15s | then type your next command]\x1b[0m", ct);

            var sb = new StringBuilder(width * 20);

            try
            {
                while (!animCts.Token.IsCancellationRequested)
                {
                    sb.Clear();

                    for (int col = 0; col < width; col++)
                    {
                        ticks[col]++;
                        if (ticks[col] < speeds[col]) continue;
                        ticks[col] = 0;

                        int head = columns[col];
                        int trail = head - 1;
                        int erase = trail - 4;

                        if (head >= 1 && head <= height)
                        {
                            char g = GlyphSet[rng.Next(GlyphSet.Length)];
                            sb.Append($"\x1b[{head};{col + 1}H\x1b[32;1m{g}\x1b[0m");
                        }

                        if (trail >= 1 && trail <= height)
                        {
                            char g = GlyphSet[rng.Next(GlyphSet.Length)];
                            sb.Append($"\x1b[{trail};{col + 1}H\x1b[32;2m{g}\x1b[0m");
                        }

                        if (erase >= 1 && erase <= height)
                        {
                            sb.Append($"\x1b[{erase};{col + 1}H ");
                        }

                        columns[col] = (head >= height) ? rng.Next(0, height / 3) : head + 1;
                    }

                    await WriteRawAsync(stream, encoding, sb.ToString(), animCts.Token);
                    await stream.FlushAsync(animCts.Token);
                    await Task.Delay(FrameDelayMs, animCts.Token);
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                await WriteRawAsync(stream, encoding, "\x1b[?25h\x1b[0m\x1b[2J\x1b[H", CancellationToken.None);
                await stream.FlushAsync(CancellationToken.None);
            }
        }

        private static async Task WriteRawAsync(Stream stream, Encoding enc, string text, CancellationToken ct)
            => await stream.WriteAsync(enc.GetBytes(text), ct);
    }
}
