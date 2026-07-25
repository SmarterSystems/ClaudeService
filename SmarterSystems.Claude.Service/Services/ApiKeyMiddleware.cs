using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SmarterSystems.Claude.Service.Services
{
    public sealed class ApiKeyMiddleware
    {
        public const string HeaderName = "X-API-Key";

        private readonly RequestDelegate _next;
        private readonly IConfiguration _configuration;

        public ApiKeyMiddleware(RequestDelegate next, IConfiguration configuration)
        {
            _next = next;
            _configuration = configuration;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (_configuration.GetValue("Auth:Disabled", false))
            {
                await _next(context);
                return;
            }

            var configuredKey = _configuration["Auth:ApiKey"];
            if (string.IsNullOrWhiteSpace(configuredKey))
            {
                await WriteError(
                    context,
                    StatusCodes.Status500InternalServerError,
                    "Auth:ApiKey is not configured. Set it, or set Auth:Disabled to true when the service is reachable only from localhost.");
                return;
            }

            if (!context.Request.Headers.TryGetValue(HeaderName, out var provided)
                || !IsMatch(configuredKey, provided.ToString()))
            {
                await WriteError(context, StatusCodes.Status401Unauthorized, $"Missing or invalid {HeaderName} header.");
                return;
            }

            await _next(context);
        }

        private static bool IsMatch(string expected, string actual)
        {
            if (string.IsNullOrEmpty(actual)) return false;
            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expected),
                Encoding.UTF8.GetBytes(actual));
        }

        private static Task WriteError(HttpContext context, int statusCode, string message)
        {
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/json";
            return context.Response.WriteAsync(JsonSerializer.Serialize(new { hasError = true, error = message }));
        }
    }
}
