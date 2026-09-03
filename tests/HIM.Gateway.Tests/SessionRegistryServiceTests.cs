using HIM.Gateway.Services.SSH;
using HIM.Gateway.Services.SSH.Interfaces;
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
}
