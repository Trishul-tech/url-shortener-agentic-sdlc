using MediatR;
using Microsoft.AspNetCore.RateLimiting;
using UrlShortener.Api.Contracts;
using UrlShortener.Api.Middleware;
using UrlShortener.Application.UrlShortening.CreateShortUrl;
using UrlShortener.Application.UrlShortening.DeactivateShortUrl;
using UrlShortener.Application.UrlShortening.ResolveShortUrl;

namespace UrlShortener.Api.Endpoints;

public static class UrlEndpoints
{
    public static IEndpointRouteBuilder MapUrlEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/urls").WithTags("Urls");

        group.MapPost("/", async (CreateShortUrlRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new CreateShortUrlCommand(
                request.TargetUrl, request.CustomAlias, request.OwnerId, request.ExpiresAtUtc), ct);

            if (!result.IsSuccess)
                return ResultMapping.ToProblem(result);

            var v = result.Value!;
            var response = new CreateShortUrlResponse(v.Code, v.ShortUrl, v.TargetUrl, v.CreatedAtUtc, v.ExpiresAtUtc);
            return Results.Created($"/api/v1/urls/{v.Code}", response);
        })
        .WithName("CreateShortUrl")
        .WithSummary("Create a shortened URL, optionally with a custom alias and expiry.")
        .Produces<CreateShortUrlResponse>(StatusCodes.Status201Created)
        .Produces<ProblemResponse>(StatusCodes.Status400BadRequest)
        .Produces<ProblemResponse>(StatusCodes.Status409Conflict)
        .RequireRateLimiting("create");

        group.MapDelete("/{code}", async (string code, string? ownerId, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new DeactivateShortUrlCommand(code, ownerId), ct);
            return result.IsSuccess ? Results.NoContent() : ResultMapping.ToProblem(result);
        })
        .WithName("DeactivateShortUrl")
        .WithSummary("Deactivate a short URL so it no longer redirects.")
        .Produces(StatusCodes.Status204NoContent)
        .Produces<ProblemResponse>(StatusCodes.Status404NotFound);

        // Redirect lives at the root, not under /api, since it's the public-facing short link surface.
        app.MapGet("/{code}", async (string code, HttpContext http, ISender sender, CancellationToken ct) =>
        {
            var referer = http.Request.Headers.Referer.ToString() is { Length: > 0 } r
                ? Uri.TryCreate(r, UriKind.Absolute, out var refUri) ? refUri.Host : null
                : null;

            var result = await sender.Send(new ResolveShortUrlQuery(
                code,
                referer,
                http.Request.Headers.UserAgent.ToString(),
                http.Connection.RemoteIpAddress?.ToString()), ct);

            return result.IsSuccess
                ? Results.Redirect(result.Value!, permanent: false)
                : ResultMapping.ToProblem(result);
        })
        .WithName("RedirectShortUrl")
        .WithSummary("Resolve a short code and redirect (302) to its target URL.")
        .ExcludeFromDescription() // avoid colliding with Swagger's catch-all route documentation
        .RequireRateLimiting("redirect");

        return app;
    }
}
