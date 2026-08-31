using System.Security.Cryptography;
using System.Text;

namespace NimbusFlow.Api.Infrastructure;

public sealed class ApiKeyMiddleware(RequestDelegate next, IConfiguration configuration)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var configuredKey = configuration["Security:ApiKey"];
        if (string.IsNullOrWhiteSpace(configuredKey) || !context.Request.Path.StartsWithSegments("/api"))
        {
            await next(context);
            return;
        }

        var suppliedKey = context.Request.Headers["X-Api-Key"].FirstOrDefault();
        if (suppliedKey is not null && KeysMatch(configuredKey, suppliedKey))
        {
            await next(context);
            return;
        }

        await Results.Problem(
            statusCode: StatusCodes.Status401Unauthorized,
            title: "A valid API key is required.",
            type: "https://httpstatuses.com/401")
            .ExecuteAsync(context);
    }

    private static bool KeysMatch(string expected, string actual)
    {
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var actualBytes = Encoding.UTF8.GetBytes(actual);
        return expectedBytes.Length == actualBytes.Length &&
               CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }
}
