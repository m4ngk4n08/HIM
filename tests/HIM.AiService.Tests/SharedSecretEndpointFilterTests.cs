using HIM.AiService.Extensions;
using HIM.AiService.Models.AI;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace HIM.AiService.Tests;

public class SharedSecretEndpointFilterTests
{
    private const string ConfiguredSecret = "correct-horse-battery-staple";

    private static SharedSecretEndpointFilter CreateFilter(string secret = ConfiguredSecret)
    {
        var settings = Options.Create(new AiSettings
        {
            Security = new SecuritySettings { SharedSecret = secret }
        });
        return new SharedSecretEndpointFilter(settings);
    }

    private static EndpointFilterInvocationContext CreateContext(string? headerValue)
    {
        var httpContext = new DefaultHttpContext();
        if (headerValue != null)
            httpContext.Request.Headers[SharedSecretEndpointFilter.HeaderName] = headerValue;

        return EndpointFilterInvocationContext.Create(httpContext);
    }

    private static EndpointFilterDelegate NextThatSucceeds() => _ => ValueTask.FromResult<object?>(Results.Ok());

    [Fact]
    public async Task InvokeAsync_CallsNext_WhenSecretMatches()
    {
        var filter = CreateFilter();
        var context = CreateContext(ConfiguredSecret);
        var nextCalled = false;

        EndpointFilterDelegate next = _ =>
        {
            nextCalled = true;
            return ValueTask.FromResult<object?>(Results.Ok());
        };

        await filter.InvokeAsync(context, next);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task InvokeAsync_ReturnsUnauthorized_WhenHeaderMissing()
    {
        var filter = CreateFilter();
        var context = CreateContext(headerValue: null);

        var result = await filter.InvokeAsync(context, NextThatSucceeds());

        Assert.IsAssignableFrom<Microsoft.AspNetCore.Http.HttpResults.UnauthorizedHttpResult>(result);
    }

    [Fact]
    public async Task InvokeAsync_ReturnsUnauthorized_WhenHeaderEmpty()
    {
        var filter = CreateFilter();
        var context = CreateContext(headerValue: "");

        var result = await filter.InvokeAsync(context, NextThatSucceeds());

        Assert.IsAssignableFrom<Microsoft.AspNetCore.Http.HttpResults.UnauthorizedHttpResult>(result);
    }

    [Fact]
    public async Task InvokeAsync_ReturnsUnauthorized_WhenSecretWrong()
    {
        var filter = CreateFilter();
        var context = CreateContext(headerValue: "not-the-secret");

        var result = await filter.InvokeAsync(context, NextThatSucceeds());

        Assert.IsAssignableFrom<Microsoft.AspNetCore.Http.HttpResults.UnauthorizedHttpResult>(result);
    }
}
