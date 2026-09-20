using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace UrlShortener.Api.Middleware;

/// <summary>
/// Optional API-key gate for mutating endpoints (create, deactivate).
/// Opt-in via the "ApiKey" configuration value (appsettings, or an
/// ApiKey environment variable) - when it is not set, this filter lets
/// every request through unchanged, so local development and the
/// existing test suite are unaffected. When it IS set, a request must
/// send a matching X-Api-Key header or it is rejected with 401. Uses a
/// fixed-time comparison so response timing cannot leak how much of the
/// key was guessed correctly.
/// </summary>
public sealed class ApiKeyFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var configuration = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        var expectedKey = configuration["ApiKey"];

        if (string.IsNullOrEmpty(expectedKey))
        {
            return await next(context);
        }

        var provided = context.HttpContext.Request.Headers["X-Api-Key"].ToString();
        var providedBytes = Encoding.UTF8.GetBytes(provided);
        var expectedBytes = Encoding.UTF8.GetBytes(expectedKey);

        var isValid = providedBytes.Length == expectedBytes.Length
            && CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);

        if (!isValid)
        {
            return Results.Problem(
                title: "Unauthorized",
                detail: "A valid X-Api-Key header is required for this endpoint.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        return await next(context);
    }
}
