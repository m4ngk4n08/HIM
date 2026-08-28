using HIM.AiService.Models.AI;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

namespace HIM.AiService.Extensions
{
    /// <summary>
    /// Requires a shared-secret header on every request, compared in fixed time.
    /// Applied at the endpoint level (see Program.cs) rather than inside the controller.
    /// </summary>
    public class SharedSecretEndpointFilter : IEndpointFilter
    {
        public const string HeaderName = "X-Ai-Shared-Secret";

        private readonly AiSettings _settings;

        public SharedSecretEndpointFilter(IOptions<AiSettings> settings)
        {
            _settings = settings.Value;
        }

        public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
        {
            var provided = context.HttpContext.Request.Headers[HeaderName].ToString();

            if (!IsValidSecret(provided))
            {
                return ValueTask.FromResult<object?>(Results.Unauthorized());
            }

            return next(context);
        }

        private bool IsValidSecret(string provided)
        {
            var expectedBytes = Encoding.UTF8.GetBytes(_settings.Security.SharedSecret);
            var providedBytes = Encoding.UTF8.GetBytes(provided);

            if (providedBytes.Length != expectedBytes.Length) return false;

            return CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);
        }
    }
}
