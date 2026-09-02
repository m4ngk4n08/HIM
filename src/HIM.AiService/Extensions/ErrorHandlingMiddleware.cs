using System.Text.Json;

namespace HIM.AiService.Extensions
{
    /// <summary>
    /// Catches unhandled exceptions and returns a generic body carrying the correlation id -
    /// never the exception message or a stack trace. The real detail goes to the log only, tagged
    /// with the same correlation id because CorrelationMiddlewareExtension's LogContext scope
    /// wraps this middleware (register this one after it). If the response has already started -
    /// the streaming chat endpoint writes its opening bracket as soon as enumeration begins -
    /// there is nothing left to rewrite into a JSON body, so this only logs and lets the
    /// connection end.
    /// </summary>
    public class ErrorHandlingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ErrorHandlingMiddleware> _logger;

        public ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                // Client disconnected mid-request - not a server error worth a response or a log entry.
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception processing {Method} {Path}",
                    context.Request.Method, context.Request.Path);

                if (context.Response.HasStarted)
                    throw;

                context.Response.Clear();
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonSerializer.Serialize(new
                {
                    error = "An unexpected error occurred.",
                    correlationId = context.TraceIdentifier
                }));
            }
        }
    }
}
