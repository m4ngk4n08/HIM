using Microsoft.Extensions.DependencyInjection;

namespace HIM.Gateway.Tests;

/// <summary>
/// SshServerListener.RunTuiInScopeAsync (the method that wraps ITuiEngine.RunAsync per shell
/// channel) follows the exact shape exercised here:
///
///     await using var scope = _serviceScopeFactory.CreateAsyncScope();
///     var svc = scope.ServiceProvider.GetRequiredService&lt;T&gt;();
///     await svc.DoWorkAsync(ct);
///
/// SshChannel comes from the SSH library and can't be constructed in a unit test, so this
/// exercises the disposal guarantee of that exact "await using" pattern directly instead -
/// proving the scope (and therefore every per-session service resolved from it) is disposed
/// whether the awaited work completes normally, throws, or is cancelled.
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

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddScoped<DisposalProbe>();
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });
    }

    [Fact]
    public async Task Scope_IsDisposed_WhenWorkCompletesNormally()
    {
        using var provider = BuildProvider();
        var factory = provider.GetRequiredService<IServiceScopeFactory>();
        DisposalProbe probe;

        await using (var scope = factory.CreateAsyncScope())
        {
            probe = scope.ServiceProvider.GetRequiredService<DisposalProbe>();
            await Task.CompletedTask; // stand-in for tuiEngine.RunAsync(...)
        }

        Assert.True(probe.Disposed);
    }

    [Fact]
    public async Task Scope_IsDisposed_WhenWorkThrows()
    {
        using var provider = BuildProvider();
        var factory = provider.GetRequiredService<IServiceScopeFactory>();
        DisposalProbe? probe = null;

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var scope = factory.CreateAsyncScope();
            probe = scope.ServiceProvider.GetRequiredService<DisposalProbe>();
            throw new InvalidOperationException("simulated channel failure");
        });

        Assert.True(probe!.Disposed);
    }

    [Fact]
    public async Task Scope_IsDisposed_WhenWorkIsCancelled()
    {
        using var provider = BuildProvider();
        var factory = provider.GetRequiredService<IServiceScopeFactory>();
        DisposalProbe? probe = null;

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await using var scope = factory.CreateAsyncScope();
            probe = scope.ServiceProvider.GetRequiredService<DisposalProbe>();
            await Task.Delay(Timeout.Infinite, cts.Token); // stand-in for a cancelled RunAsync
        });

        Assert.True(probe!.Disposed);
    }
}
