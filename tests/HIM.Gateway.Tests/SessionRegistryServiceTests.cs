using HIM.Gateway.Services.SSH;
using HIM.Gateway.Services.SSH.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace HIM.Gateway.Tests;

/// <summary>
/// Task 24C: the registry itself, independent of SshServerListener's wiring (that half is
/// SessionRegistryLifecycleTests). Proves the leak case at the data-structure level and that
/// timestamps come from the injected TimeProvider, not the wall clock.
/// </summary>
public class SessionRegistryServiceTests
{
    [Fact]
    public void RegisterThenDeregister_LeavesTheRegistryEmpty()
    {
        var registry = new SessionRegistryService(new FakeTimeProvider());

        registry.Register("session-1", "203.0.113.9");
        registry.Deregister("session-1");

        Assert.Empty(registry.GetActiveSessions());
    }

    [Fact]
    public void Deregister_OfANeverRegisteredSession_DoesNotThrow()
    {
        var registry = new SessionRegistryService(new FakeTimeProvider());

        var exception = Record.Exception(() => registry.Deregister("never-registered"));

        Assert.Null(exception);
    }

    [Fact]
    public void TwoRegisteredSessions_BothAppear_WithTheirOwnIpAddress()
    {
        // Each session's row is a plain value tied to that session's own id - no shared state
        // that could leak one session's data onto another's row.
        var registry = new SessionRegistryService(new FakeTimeProvider());

        registry.Register("session-a", "203.0.113.9");
        registry.Register("session-b", "198.51.100.4");

        var sessions = registry.GetActiveSessions();
        Assert.Equal(2, sessions.Count);
        Assert.Contains(sessions, s => s.SessionId == "session-a" && s.IpAddress == "203.0.113.9");
        Assert.Contains(sessions, s => s.SessionId == "session-b" && s.IpAddress == "198.51.100.4");
    }

    [Fact]
    public void ConnectedAtUtc_IsDrivenByTimeProvider_NotWallClock()
    {
        var timeProvider = new FakeTimeProvider();
        var registry = new SessionRegistryService(timeProvider);
        var registeredAt = timeProvider.GetUtcNow().UtcDateTime;

        timeProvider.Advance(TimeSpan.FromMinutes(5));
        registry.Register("session-1", "203.0.113.9");

        var snapshot = Assert.Single(registry.GetActiveSessions());
        Assert.Equal(registeredAt + TimeSpan.FromMinutes(5), snapshot.ConnectedAtUtc);
    }

    [Fact]
    public void TwoConcurrentSessions_KeepTheirOwnUserSessionState_AndBothRowsAppear()
    {
        // The registry is a singleton, so if it ever held anything scoped, two visitors connected
        // at the same time would end up sharing one session's state - and ValidateScopes would not
        // catch it, because putting a scoped object into a singleton's dictionary at runtime is
        // legal DI, just wrong. Two real DI scopes are used here (rather than made-up id strings)
        // so the isolation being asserted is the one production actually relies on.
        using var provider = GatewayServiceProviderFactory.Build();
        var registry = provider.GetRequiredService<ISessionRegistryService>();

        using var scopeA = provider.CreateScope();
        using var scopeB = provider.CreateScope();
        var stateA = scopeA.ServiceProvider.GetRequiredService<UserSessionState>();
        var stateB = scopeB.ServiceProvider.GetRequiredService<UserSessionState>();

        // Neither session sees the other's UserSessionState: different instances, different ids.
        Assert.NotSame(stateA, stateB);
        Assert.NotEqual(stateA.SessionId, stateB.SessionId);

        registry.Register(stateA.SessionId, "203.0.113.9");
        registry.Register(stateB.SessionId, "198.51.100.4");

        // Both appear, each row keyed to its own session - the registry only ever holds the id
        // string, never the UserSessionState it came from.
        var sessions = registry.GetActiveSessions();
        Assert.Equal(2, sessions.Count);
        Assert.Contains(sessions, s => s.SessionId == stateA.SessionId && s.IpAddress == "203.0.113.9");
        Assert.Contains(sessions, s => s.SessionId == stateB.SessionId && s.IpAddress == "198.51.100.4");
    }
}
