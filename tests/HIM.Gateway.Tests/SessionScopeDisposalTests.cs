using HIM.Gateway.Extensions;
using HIM.Gateway.Models;
using HIM.Gateway.Services.SSH;
using HIM.Gateway.Services.SSH.Interfaces;
using HIM.Gateway.Services.SSH.Interfaces.IGates;
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

    private sealed class ListenerFixture : IDisposable
    {
        public required SshServerListener Listener { get; init; }
        public required ServiceProvider ProbeProvider { get; init; }
        public required ServiceProvider DependencyProvider { get; init; }

        public void Dispose()
        {
            ProbeProvider.Dispose();
            DependencyProvider.Dispose();
        }
    }

    private static ListenerFixture BuildListener()
    {
        var probeServices = new ServiceCollection();
        probeServices.AddScoped<DisposalProbe>();
        var probeProvider = probeServices.BuildServiceProvider(ServiceExtensions.ContainerValidationOptions);

        var dependencyProvider = GatewayServiceProviderFactory.Build();

        var listener = new SshServerListener(
            probeProvider.GetRequiredService<IServiceScopeFactory>(),
            dependencyProvider.GetRequiredService<IHostKeyService>(),
            dependencyProvider.GetRequiredService<IAuthenticationService>(),
            dependencyProvider.GetServices<IConnectionGate>(),
            dependencyProvider.GetRequiredService<IConnectionSlotGate>(),
            dependencyProvider.GetRequiredService<ILogger<SshServerListener>>(),
            dependencyProvider.GetRequiredService<IOptions<SshSettings>>(),
            dependencyProvider.GetRequiredService<IConnectionMetricsService>());

        return new ListenerFixture
        {
            Listener = listener,
            ProbeProvider = probeProvider,
            DependencyProvider = dependencyProvider
        };
    }

    [Fact]
    public async Task Scope_IsDisposed_WhenWorkCompletesNormally()
    {
        using var fixture = BuildListener();
        DisposalProbe? probe = null;

        await fixture.Listener.RunInScopeAsync(async (sp, ct) =>
        {
            probe = sp.GetRequiredService<DisposalProbe>();
            await Task.CompletedTask; // stand-in for tuiEngine.RunAsync(...)
        }, CancellationToken.None);

        Assert.True(probe!.Disposed);
    }

    [Fact]
    public async Task Scope_IsDisposed_WhenWorkThrows()
    {
        using var fixture = BuildListener();
        DisposalProbe? probe = null;

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await fixture.Listener.RunInScopeAsync((sp, ct) =>
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
        using var fixture = BuildListener();
        DisposalProbe? probe = null;

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await fixture.Listener.RunInScopeAsync(async (sp, ct) =>
            {
                probe = sp.GetRequiredService<DisposalProbe>();
                await Task.Delay(Timeout.Infinite, ct); // stand-in for a cancelled RunAsync
            }, cts.Token);
        });

        Assert.True(probe!.Disposed);
    }
}
