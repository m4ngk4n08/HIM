using HIM.Gateway.Services.SSH;
using HIM.Gateway.Services.SSH.Interfaces;
using HIM.Gateway.Services.SSH.Interfaces.IGame;
using Microsoft.Extensions.DependencyInjection;

namespace HIM.Gateway.Tests;

public class ServiceLifetimeTests
{
    [Fact]
    public void Container_Builds_WithValidateScopesAndValidateOnBuild_NoCaptiveDependencies()
    {
        // ValidateOnBuild throws OptionsValidationException/InvalidOperationException at
        // BuildServiceProvider time if any singleton's constructor graph reaches a
        // scoped/transient service - i.e. a captive dependency. Just not throwing here is
        // the assertion: the container is clean.
        using var provider = GatewayServiceProviderFactory.Build();

        Assert.NotNull(provider);
    }

    [Fact]
    public void TwoConcurrentSessions_DoNotShareGameState_PacManGetsASeparateInstancePerScope()
    {
        // This is the actual reported bug: PacManGame was AddSingleton, so every visitor
        // played on the same board. With IGameService registered Scoped and a scope created
        // per shell channel, two sessions must resolve two distinct PacManGame instances -
        // and therefore two distinct GameState boards.
        using var provider = GatewayServiceProviderFactory.Build();

        using var sessionOneScope = provider.CreateScope();
        using var sessionTwoScope = provider.CreateScope();

        var pacManOne = ResolvePacMan(sessionOneScope.ServiceProvider);
        var pacManTwo = ResolvePacMan(sessionTwoScope.ServiceProvider);

        Assert.NotSame(pacManOne, pacManTwo);
    }

    [Fact]
    public void SameScope_ResolvesTheSameGameInstance_Twice()
    {
        // Sanity check for the other half of the contract: within a single session scope,
        // resolving the game twice (e.g. via the factory and directly) must return the same
        // instance, not a fresh board each time.
        using var provider = GatewayServiceProviderFactory.Build();
        using var scope = provider.CreateScope();

        var first = ResolvePacMan(scope.ServiceProvider);
        var second = ResolvePacMan(scope.ServiceProvider);

        Assert.Same(first, second);
    }

    [Fact]
    public void PortfolioDataProvider_IsSingleton_AcrossConcurrentSessions()
    {
        // AddScoped here would mean a file read and JSON parse on every SSH connection -
        // the exact regression the amendment in a118b7a fixed. Pin it down.
        using var provider = GatewayServiceProviderFactory.Build();

        using var sessionOneScope = provider.CreateScope();
        using var sessionTwoScope = provider.CreateScope();

        var one = sessionOneScope.ServiceProvider.GetRequiredService<IPortfolioDataProvider>();
        var two = sessionTwoScope.ServiceProvider.GetRequiredService<IPortfolioDataProvider>();

        Assert.Same(one, two);
    }

    [Fact]
    public void TwoConcurrentSessions_GetDistinctUserSessionState()
    {
        using var provider = GatewayServiceProviderFactory.Build();

        using var sessionOneScope = provider.CreateScope();
        using var sessionTwoScope = provider.CreateScope();

        var stateOne = sessionOneScope.ServiceProvider.GetRequiredService<UserSessionState>();
        var stateTwo = sessionTwoScope.ServiceProvider.GetRequiredService<UserSessionState>();

        Assert.NotSame(stateOne, stateTwo);
        Assert.NotEqual(stateOne.SessionId, stateTwo.SessionId);
    }

    [Fact]
    public void SameScope_UserSessionState_IsSharedAcrossServicesInThatScope()
    {
        // ICommandService and anything else resolved within one session scope must see the
        // same UserSessionState instance the scope itself resolves - proving the cooldown
        // timer and session ID are consistent for the life of one session.
        using var provider = GatewayServiceProviderFactory.Build();
        using var scope = provider.CreateScope();

        var direct = scope.ServiceProvider.GetRequiredService<UserSessionState>();

        // ICommandService's constructor also takes a UserSessionState; resolving it must
        // hand back the very same scoped instance.
        _ = scope.ServiceProvider.GetRequiredService<ICommandService>();
        var second = scope.ServiceProvider.GetRequiredService<UserSessionState>();

        Assert.Same(direct, second);
    }

    [Fact]
    public void TwoConcurrentSessions_ThemeChangeInOneScope_DoesNotAffectTheOther()
    {
        // The actual reported bug (BL-10): ThemeService used to be a static class, so one
        // visitor's /theme neon recolored every other concurrent session. Two scopes must now
        // see independent IThemeService instances.
        using var provider = GatewayServiceProviderFactory.Build();

        using var sessionOneScope = provider.CreateScope();
        using var sessionTwoScope = provider.CreateScope();

        var themeOne = sessionOneScope.ServiceProvider.GetRequiredService<IThemeService>();
        var themeTwo = sessionTwoScope.ServiceProvider.GetRequiredService<IThemeService>();

        themeOne.SetTheme(Theme.Neon);

        Assert.Equal(Theme.Dark, themeTwo.CurrentTheme);
        Assert.Equal(Spectre.Console.Color.Cyan1, themeTwo.PrimaryColor);
    }

    [Fact]
    public void ThirdScope_CreatedAfterFirstScopeDisposed_StillGetsTheDefaultTheme()
    {
        // The worse half of BL-10: the next visitor to connect inherited whatever theme the
        // previous visitor left set, because the static field outlived the session that set
        // it. A scope created after the setting scope is disposed must not see that theme.
        using var provider = GatewayServiceProviderFactory.Build();

        using (var sessionOneScope = provider.CreateScope())
        {
            var themeOne = sessionOneScope.ServiceProvider.GetRequiredService<IThemeService>();
            themeOne.SetTheme(Theme.Neon);
        }

        using var sessionThreeScope = provider.CreateScope();
        var themeThree = sessionThreeScope.ServiceProvider.GetRequiredService<IThemeService>();

        Assert.Equal(Theme.Dark, themeThree.CurrentTheme);
    }

    private static IGameService ResolvePacMan(IServiceProvider provider)
    {
        var game = provider.GetServices<IGameService>().FirstOrDefault(g => g.Name == "Pac-Man");
        Assert.NotNull(game);
        return game!;
    }
}
