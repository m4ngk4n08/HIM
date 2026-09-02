using HIM.AiService.Models.AI;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace HIM.AiService.Security
{
    /// <summary>
    /// Rejects unauthenticated requests before they reach rate limiting or routing, so an
    /// unauthenticated flood cannot burn through the rate-limit quota. Must be registered
    /// ahead of app.UseRateLimiter() in Program.cs.
    /// </summary>
    public class SharedSecretMiddleware
    {
        public const string HeaderName = "X-Ai-Shared-Secret";

        private readonly RequestDelegate _next;

        public SharedSecretMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context, IOptions<AiSettings> settings)
        {
            // SEC-08: /health/live and /health/ready are probed by the container runtime /
            // orchestrator, which has no shared secret to send. Nothing behind them exposes
            // knowledge-base content - only a boolean up/ready state.
            if (context.Request.Path.StartsWithSegments("/health"))
            {
                await _next(context);
                return;
            }

            var provided = context.Request.Headers[HeaderName].ToString();

            if (!SharedSecretValidator.IsValid(provided, settings.Value.Security.SharedSecret))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            await _next(context);
        }
    }
}
