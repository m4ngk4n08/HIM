using HIM.Gateway.Models;
using HIM.Gateway.Models.Knowledge;
using HIM.Gateway.Services.SSH;
using HIM.Gateway.Services.SSH.Commands;
using HIM.Gateway.Services.SSH.Interfaces;
using Microsoft.Extensions.Time.Testing;
using Spectre.Console;

namespace HIM.Gateway.Tests;

/// <summary>
/// Task 24C: /who renders the session registry. The two security requirements (masked IPs,
/// no SSH username) are what's under test here, along with the own-row highlight and the
/// FakeTimeProvider-driven duration - the registry's own data-structure behavior is covered by
/// SessionRegistryServiceTests and SessionRegistryLifecycleTests.
/// </summary>
public class WhoCommandTests
{
    private sealed class FakeSessionRegistryService : ISessionRegistryService
    {
        private readonly List<SessionSnapshot> _sessions = new();
        public void Seed(SessionSnapshot session) => _sessions.Add(session);
        public void Register(string sessionId, string ipAddress) { }
        public void Deregister(string sessionId) { }
        public IReadOnlyList<SessionSnapshot> GetActiveSessions() => _sessions;
    }

    private static async Task<string> RunWhoAsync(
        FakeSessionRegistryService registry, FakeTimeProvider timeProvider, string callerSessionId = "session")
    {
        var command = new WhoCommand(registry, new ThemeService(), timeProvider);

        var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(writer)
        });
        console.Profile.Width = 240;
        using var stream = new MemoryStream();
        var context = new CommandContext(console, stream, "/who", new PortfolioData(), callerSessionId, CancellationToken.None);

        await command.ExecuteAsync(context);
        return writer.ToString();
    }

    [Fact]
    public async Task NoOtherSessions_SaysJustYou()
    {
        var registry = new FakeSessionRegistryService();
        var timeProvider = new FakeTimeProvider();
        registry.Seed(new SessionSnapshot("session", "203.0.113.9", timeProvider.GetUtcNow().UtcDateTime));

        var output = await RunWhoAsync(registry, timeProvider, callerSessionId: "session");

        Assert.Contains("Just you right now.", output);
    }

    [Fact]
    public async Task TwoSessions_SaysTwoPeopleConnected_AndMarksTheCallersOwnRow()
    {
        var registry = new FakeSessionRegistryService();
        var timeProvider = new FakeTimeProvider();
        var connectedAt = timeProvider.GetUtcNow().UtcDateTime;
        registry.Seed(new SessionSnapshot("11111111-aaaa-bbbb-cccc-000000000001", "203.0.113.9", connectedAt));
        registry.Seed(new SessionSnapshot("22222222-aaaa-bbbb-cccc-000000000002", "198.51.100.4", connectedAt));

        var output = await RunWhoAsync(registry, timeProvider, callerSessionId: "11111111-aaaa-bbbb-cccc-000000000001");

        Assert.Contains("2 people connected right now.", output);
        Assert.Contains("(you)", output);
        // Only the caller's row gets the marker - not the other visitor's.
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(output, "\\(you\\)"));
    }

    [Fact]
    public async Task Ipv4AndIpv6Sessions_NeverRenderTheFullAddress()
    {
        var registry = new FakeSessionRegistryService();
        var timeProvider = new FakeTimeProvider();
        var connectedAt = timeProvider.GetUtcNow().UtcDateTime;
        registry.Seed(new SessionSnapshot("session-v4", "203.0.113.9", connectedAt));
        registry.Seed(new SessionSnapshot("session-v6", "2001:db8::1", connectedAt));

        var output = await RunWhoAsync(registry, timeProvider, callerSessionId: "session-v4");

        Assert.DoesNotContain("203.0.113.9", output);
        Assert.DoesNotContain("2001:db8::1", output);
        Assert.Contains("203.0.113.x", output);
        Assert.Contains("2001:db8::x", output);
    }

    [Fact]
    public async Task ConnectionDuration_IsDrivenByTimeProvider_NotWallClock()
    {
        var registry = new FakeSessionRegistryService();
        var timeProvider = new FakeTimeProvider();
        var connectedAt = timeProvider.GetUtcNow().UtcDateTime;
        registry.Seed(new SessionSnapshot("session", "203.0.113.9", connectedAt));

        timeProvider.Advance(TimeSpan.FromMinutes(90));

        var output = await RunWhoAsync(registry, timeProvider, callerSessionId: "session");

        Assert.Contains("1h 30m", output);
    }
}
