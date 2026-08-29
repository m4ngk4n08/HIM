using HIM.Gateway.Models;
using HIM.Gateway.Services.ServiceModel;
using HIM.Gateway.Services.SSH.Interfaces;
using HIM.Gateway.Services.SSH.Interfaces.ICommandDispatcher;
using Microsoft.DevTunnels.Ssh;
using Microsoft.Extensions.Options;
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
        // Terminals emit "window-change" on every frame of a drag-resize. Debounce so a drag
        // doesn't queue dozens of full re-renders on a 1-vCPU box.
        private static readonly TimeSpan ResizeDebounceDelay = TimeSpan.FromMilliseconds(100);

        private readonly IConsoleEngineService _consoleEngineService;
        private readonly ITerminalLayoutService _terminalLayoutService;
        private readonly ICommandDispatcherHelper _commandDispatcher;

        // ITuiEngine has been resolved from a per-shell-channel DI scope since 8904cf3, so
        // exactly one TuiEngine instance ever serves one channel - these are plain fields, not
        // the ConcurrentDictionary<SshChannel, IAnsiConsole> this used to be keyed on (that was a
        // leftover from when the engine was a singleton, and nothing ever added to it: HandleResize
        // could never find a console). RunAsync's thread writes these once at startup and clears
        // them in its finally; HandleResize is invoked from the SSH library's request-handling
        // thread and reads them. A plain reference write is atomic but not guaranteed visible
        // across threads without a memory barrier, so both sides go through Volatile rather than
        // a lock - the fields are simple pointer swaps, not multi-step state, so a lock would only
        // add contention for no extra safety here.
        private IAnsiConsole? _console;
        private Stream? _sshStream;

        // Resize debounce/no-op-suppression state. Guarded by a lock because updating it is a
        // multi-step read-modify-write (compare dimensions, replace the pending CTS) that must be
        // atomic across concurrent window-change events.
        private readonly object _resizeGate = new();
        private int _lastWidth = -1;
        private int _lastHeight = -1;
        private CancellationTokenSource? _pendingResizeCts;

        public TuiEngine(
            IConsoleEngineService consoleEngineService,
            ITerminalLayoutService terminalLayoutService,
            ICommandDispatcherHelper commandDispatcher)
        {
            _consoleEngineService = consoleEngineService;
            _terminalLayoutService = terminalLayoutService;
            _commandDispatcher = commandDispatcher;
        }

        public void HandleResize(SshChannel channel, uint width, uint height)
        {
            var console = Volatile.Read(ref _console);
            var stream = Volatile.Read(ref _sshStream);
            if (console is null || stream is null)
            {
                // Either the window-change arrived before "shell" finished starting the engine
                // (the race the ?. in SshServerListener guards against), or the session has
                // already torn down. Either way there's nothing to resize.
                return;
            }

            int newWidth = (int)width;
            int newHeight = (int)height;

            CancellationTokenSource cts;
            lock (_resizeGate)
            {
                if (newWidth == _lastWidth && newHeight == _lastHeight)
                    return; // No-op size change - ignore it rather than re-render for nothing.

                _lastWidth = newWidth;
                _lastHeight = newHeight;

                _pendingResizeCts?.Cancel();
                _pendingResizeCts?.Dispose();
                cts = new CancellationTokenSource();
                _pendingResizeCts = cts;
            }

            _ = Task.Run(() => ApplyResizeAsync(console, stream, newWidth, newHeight, cts));
        }

        private async Task ApplyResizeAsync(
            IAnsiConsole console, Stream stream, int width, int height, CancellationTokenSource cts)
        {
            try
            {
                // Debounce: wait for the dimensions to settle before doing the (relatively
                // expensive) full re-render. A newer resize cancels this token and starts its own.
                await Task.Delay(ResizeDebounceDelay, cts.Token);

                console.Profile.Width = width;
                console.Profile.Height = height;

                // Re-run the layout for the new size instead of destroying it: this replaces the
                // old behaviour of clearing the screen and printing "(Terminal resized to WxH)"
                // plus a bare prompt, which threw away the chrome and any partially-typed input.
                await _terminalLayoutService.InitializeTerminalLayoutAsync(console, stream, cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Superseded by a newer resize event, or the session ended - not an error.
            }
            catch (Exception ex) when (IsTransportException(ex))
            {
                // Client disconnected mid-resize - nothing left to redraw.
            }
        }

        private static bool IsTransportException(Exception ex)
        {
            if (ex is AggregateException ae)
            {
                ex = ae.Flatten().InnerException ?? ex;
            }
            return ex is IOException
                || ex is ObjectDisposedException
                || (ex is InvalidOperationException ioe && ioe.Message.Contains("EOF"));
        }

        public async Task RunAsync(SshChannel channel, uint width, uint height, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(channel);

            SshStream? sshStream = null;
            try
            {
                // 1. Establish the bidirectional network stream over the SSH channel
                sshStream = new SshStream(channel);

                // 2. Initialize Spectre.Console with a custom output bridge to the SSH stream
                var console = _consoleEngineService.CreateConsole(sshStream, width, height);

                lock (_resizeGate)
                {
                    _lastWidth = (int)width;
                    _lastHeight = (int)height;
                }
                Volatile.Write(ref _console, console);
                Volatile.Write(ref _sshStream, sshStream);

                // 3. Execute the Visual Initialization (Splash Screen)
                await _consoleEngineService.RenderSplashScreenAsync(console, sshStream, ct);

                // 4. Start the Interactive Command & AI Chat Loop
                await _consoleEngineService.HandleInteractionLoopAsync(console, sshStream, ct);
            }
            catch (OperationCanceledException)
            {
                // Expected teardown on client disconnect or server shutdown
                Console.WriteLine($"[TUI] Clean exit for channel {channel.ChannelId}.");
            }
            catch (Exception ex) when (
                ex is IOException ||
                ex is ObjectDisposedException ||
                (ex is InvalidOperationException && ex.Message.Contains("EOF")) ||
                (ex is AggregateException ae && (
                    ae.Flatten().InnerException is IOException ||
                    ae.Flatten().InnerException is ObjectDisposedException ||
                    (ae.Flatten().InnerException is InvalidOperationException innerIoe && innerIoe.Message.Contains("EOF"))
                ))
            )
            {
                // Transport-layer disconnects are normal in SSH. Treat as a clean exit.
                Console.WriteLine($"[TUI] Session {channel.ChannelId} disconnected (Transport EOF).");
            }
            catch (Exception ex)
            {
                // Error boundary to protect the Gateway process from actual application bugs
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(ex.ToString());
                Console.ResetColor();
            }
            finally
            {
                // Stop any in-flight/pending resize before the stream goes away.
                lock (_resizeGate)
                {
                    _pendingResizeCts?.Cancel();
                    _pendingResizeCts?.Dispose();
                    _pendingResizeCts = null;
                }

                Volatile.Write(ref _console, null);
                Volatile.Write(ref _sshStream, null);

                if (sshStream is not null)
                {
                    // DECSTBM persists in the client's own terminal after the session ends - reset
                    // it before the stream closes on every exit path (normal, exception, or
                    // cancellation), or a visitor is left with a broken scroll region. Best-effort:
                    // the connection may already be gone by this point.
                    try
                    {
                        await _commandDispatcher.ResetScrollingRegionAsync(sshStream, CancellationToken.None);
                    }
                    catch { /* connection already closed - nothing to reset */ }

                    // Dispose exactly once here. The old code disposed sshStream at the end of the
                    // try block *and* held it in a using statement, which disposed it a second time
                    // on every path - including the normal one.
                    sshStream.Dispose();
                }
            }
        }

    }

}
