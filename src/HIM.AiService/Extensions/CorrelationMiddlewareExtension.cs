using Serilog.Context;

namespace HIM.AiService.Extensions
{
    public class CorrelationMiddlewareExtension
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<CorrelationMiddlewareExtension> _logger;

        public CorrelationMiddlewareExtension(
            RequestDelegate next,
            ILogger<CorrelationMiddlewareExtension> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task Invoke(HttpContext context)
        {
            // Try to get from header, else generate
            var correlationId = context.Request.Headers.TryGetValue("X-Request-Id", out var header)
                ? header.FirstOrDefault()
                : Guid.NewGuid().ToString();

            context.TraceIdentifier = correlationId;

            using(LogContext.PushProperty("CorrelationId", correlationId))
            using (LogContext.PushProperty("Source", "HTTP"))
            {
                await _next(context);
            }
        }
    }
}
