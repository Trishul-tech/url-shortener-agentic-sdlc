using Microsoft.AspNetCore.Diagnostics;
using UrlShortener.Api.Contracts;
using UrlShortener.Domain.Exceptions;

namespace UrlShortener.Api.Middleware;

/// <summary>
/// Last-resort handler for exceptions that escape MediatR handlers
/// (domain invariant violations, unexpected infra failures). Application
/// code should prefer Result&lt;T&gt; for expected failures; this exists
/// so an unhandled exception never leaks a stack trace to a caller.
/// </summary>
public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) => _logger = logger;

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken ct)
    {
        var (status, title) = exception switch
        {
            DomainException => (StatusCodes.Status400BadRequest, "Domain Rule Violation"),
            _ => (StatusCodes.Status500InternalServerError, "Internal Server Error")
        };

        _logger.LogError(exception, "Unhandled exception on {Path}", httpContext.Request.Path);

        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(
            new ProblemResponse(title, exception.GetType().Name, exception.Message), cancellationToken: ct);

        return true;
    }
}
