using HIM.Gateway.Extensions;
using HIM.Gateway.Models;
using HIM.Gateway.Services.SSH;
using HIM.Gateway.Services.SSH.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HIM.Gateway.Tests;

/// <summary>
/// Exercises SshServerListener.RunInScopeAsync directly - the internal seam that
/// RunTuiInScopeAsync (invoked per shell channel) delegates to. Proves the scope (and
/// therefore every per-session service resolved from it) is disposed whether the work
/// delegate completes normally, throws, or is cancelled.
/// </summary>
public class SessionScopeDisposalTests
{
    private sealed class DisposalProbe : IAsyncDisposable
    {
        public bool Disposed { get; private set; }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private static SshServerListener BuildListener(out IServiceProvider probeProvider)
    {
        var probeServices = new ServiceCollection();
        probeServices.AddScoped<DisposalProbe>();
        probeProvider = probeServices.BuildServiceProvider(ServiceExtensions.ContainerValidationOptions);

        using var dependencyProvider = GatewayServiceProviderFactory.Build();

        return new SshServerListener(
            probeProvider.GetRequiredService<IServiceScopeFactory>(),
            dependencyProvider.GetRequiredService<IHostKeyService>(),
            dependencyProvider.GetRequiredService<IAuthenticationService>(),
            dependencyProvider.GetRequiredService<IIpBanService>(),
            dependencyProvider.GetRequiredService<ILogger<SshServerListener>>(),
            dependencyProvider.GetRequiredService<IOptions<SshSettings>>());
    }

    [Fact]
    public async Task Scope_IsDisposed_WhenWorkCompletesNormally()
    {
        var listener = BuildListener(out var probeProvider);
        using var _ = probeProvider as IDisposable;
        DisposalProbe? probe = null;

        await listener.RunInScopeAsync(async (sp, ct) =>
        {
            probe = sp.GetRequiredService<DisposalProbe>();
            await Task.CompletedTask; // stand-in for tuiEngine.RunAsync(...)
        }, CancellationToken.None);

        Assert.True(probe!.Disposed);
    }

    [Fact]
    public async Task Scope_IsDisposed_WhenWorkThrows()
    {
        var listener = BuildListener(out var probeProvider);
        using var _ = probeProvider as IDisposable;
        DisposalProbe? probe = null;

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await listener.RunInScopeAsync((sp, ct) =>
            {
                probe = sp.GetRequiredService<DisposalProbe>();
                throw new InvalidOperationException("simulated channel failure");
            }, CancellationToken.None);
        });

        Assert.True(probe!.Disposed);
    }

    [Fact]
    public async Task Scope_IsDisposed_WhenWorkIsCancelled()
    {
        var listener = BuildListener(out var probeProvider);
        using var _ = probeProvider as IDisposable;
        DisposalProbe? probe = null;

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await listener.RunInScopeAsync(async (sp, ct) =>
            {
                probe = sp.GetRequiredService<DisposalProbe>();
                await Task.Delay(Timeout.Infinite, ct); // stand-in for a cancelled RunAsync
            }, cts.Token);
        });

        Assert.True(probe!.Disposed);
    }
}
