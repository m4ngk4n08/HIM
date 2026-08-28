using HIM.AiService.Models.AI;
using HIM.AiService.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace HIM.AiService.Tests;

/// <summary>
/// Regression test for the ordering bug from the Task 01 amendment: the rate limiter must not
/// run before the shared-secret check, or an unauthenticated flood burns through the quota
/// and can lock out a legitimate caller who has never even sent the wrong secret.
/// </summary>
public class SharedSecretMiddlewareOrderTests
{
    private const string Secret = "correct-horse-battery-staple";
    private const int PermitLimit = 2;

    private static async Task<WebApplication> StartAppAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        builder.Services.Configure<AiSettings>(o => o.Security.SharedSecret = Secret);

        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddFixedWindowLimiter("TestPolicy", o =>
            {
                o.PermitLimit = PermitLimit;
                o.Window = TimeSpan.FromMinutes(1);
                o.QueueLimit = 0;
            });
        });

        var app = builder.Build();

        // Same order as HIM.AiService/Program.cs: secret check before the rate limiter.
        app.UseMiddleware<SharedSecretMiddleware>();
        app.UseRateLimiter();
        app.MapGet("/test", () => Results.Ok()).RequireRateLimiting("TestPolicy");

        await app.StartAsync();
        return app;
    }

    [Fact]
    public async Task UnauthenticatedFlood_DoesNotConsumeRateLimitQuota()
    {
        await using var app = await StartAppAsync();
        var client = app.GetTestClient();

        // Send more unauthenticated requests than the rate limiter would ever permit.
        for (int i = 0; i < PermitLimit + 3; i++)
        {
            using var response = await client.GetAsync("/test");
            Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
        }

        // A legitimate, correctly-authenticated request must still succeed - the quota
        // was never touched by the unauthenticated flood above.
        using var authedRequest = new HttpRequestMessage(HttpMethod.Get, "/test");
        authedRequest.Headers.Add(SharedSecretMiddleware.HeaderName, Secret);
        using var authedResponse = await client.SendAsync(authedRequest);

        Assert.Equal(System.Net.HttpStatusCode.OK, authedResponse.StatusCode);
    }
}
