using Microsoft.AspNetCore.Http.HttpResults;
using UrlShortener.Api.Contracts;
using UrlShortener.Application.Common;

namespace UrlShortener.Api.Middleware;

/// <summary>Maps an application-layer Result&lt;T&gt; failure to the correct HTTP status/problem body.</summary>
public static class ResultMapping
{
    public static IResult ToProblem<T>(Result<T> result)
    {
        var title = result.ErrorCode switch
        {
            ErrorCodes.NotFound => "Not Found",
            ErrorCodes.Conflict => "Conflict",
            ErrorCodes.Expired => "Gone",
            ErrorCodes.Deactivated => "Gone",
            ErrorCodes.ValidationFailed => "Validation Failed",
            ErrorCodes.RateLimited => "Too Many Requests",
            _ => "Error"
        };

        var status = result.ErrorCode switch
        {
            ErrorCodes.NotFound => StatusCodes.Status404NotFound,
            ErrorCodes.Conflict => StatusCodes.Status409Conflict,
            ErrorCodes.Expired or ErrorCodes.Deactivated => StatusCodes.Status410Gone,
            ErrorCodes.ValidationFailed => StatusCodes.Status400BadRequest,
            ErrorCodes.RateLimited => StatusCodes.Status429TooManyRequests,
            _ => StatusCodes.Status500InternalServerError
        };

        return Results.Json(
            new ProblemResponse(title, result.ErrorCode ?? "UNKNOWN", result.Error ?? "An error occurred."),
            statusCode: status);
    }
}
