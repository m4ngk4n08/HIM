using HIM.AiService.Models.AI;
using HIM.AiService.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace HIM.AiService.Tests;

/// <summary>
/// Task 21C (BL-9): the /health exemption in SharedSecretMiddleware must name the two known
/// probe paths explicitly, not match any path starting with "/health". This pins that
/// boundary - a hypothetical third path under /health/* must still require the secret.
/// </summary>
public class SharedSecretMiddlewareHealthExemptionTests
{
    private const string Secret = "correct-horse-battery-staple";

    private static async Task<WebApplication> StartAppAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        builder.Services.Configure<AiSettings>(o => o.Security.SharedSecret = Secret);

        var app = builder.Build();

        app.UseMiddleware<SharedSecretMiddleware>();
        app.MapGet("/health/live", () => Results.Ok());
        app.MapGet("/health/ready", () => Results.Ok());
        app.MapGet("/health/secret", () => Results.Ok());
        app.MapGet("/healthz", () => Results.Ok());

        await app.StartAsync();
        return app;
    }

    [Fact]
    public async Task HealthLive_IsReachable_WithNoSecretHeader()
    {
        await using var app = await StartAppAsync();
        var client = app.GetTestClient();

        using var response = await client.GetAsync("/health/live");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task HealthReady_IsReachable_WithNoSecretHeader()
    {
        await using var app = await StartAppAsync();
        var client = app.GetTestClient();

        using var response = await client.GetAsync("/health/ready");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AThirdPathUnderHealth_Is401_WithNoSecretHeader()
    {
        await using var app = await StartAppAsync();
        var client = app.GetTestClient();

        using var response = await client.GetAsync("/health/secret");

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Healthz_StillRequiresTheSecret()
    {
        await using var app = await StartAppAsync();
        var client = app.GetTestClient();

        using var response = await client.GetAsync("/healthz");

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
