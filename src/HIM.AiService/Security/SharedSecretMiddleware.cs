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
