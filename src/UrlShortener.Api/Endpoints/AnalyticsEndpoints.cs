using MediatR;
using UrlShortener.Api.Contracts;
using UrlShortener.Api.Middleware;
using UrlShortener.Application.Analytics.GetUrlAnalytics;

namespace UrlShortener.Api.Endpoints;

public static class AnalyticsEndpoints
{
    public static IEndpointRouteBuilder MapAnalyticsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/urls").WithTags("Analytics");

        group.MapGet("/{code}/analytics", async (
            string code, DateTimeOffset? from, DateTimeOffset? to, ISender sender, CancellationToken ct) =>
        {
            var toUtc = to ?? DateTimeOffset.UtcNow;
            var fromUtc = from ?? toUtc.AddDays(-30);

            var result = await sender.Send(new GetUrlAnalyticsQuery(code, fromUtc, toUtc), ct);
            if (!result.IsSuccess)
                return ResultMapping.ToProblem(result);

            var v = result.Value!;
            var daily = v.DailyClicks.ToDictionary(kv => kv.Key.ToString("yyyy-MM-dd"), kv => kv.Value);

            return Results.Ok(new AnalyticsResponse(v.Code, v.TotalClicks, v.ClicksInRange, v.LastAccessedAtUtc, daily));
        })
        .WithName("GetUrlAnalytics")
        .WithSummary("Click analytics for a short URL over a date range (defaults to the last 30 days).")
        .Produces<AnalyticsResponse>(StatusCodes.Status200OK)
        .Produces<ProblemResponse>(StatusCodes.Status404NotFound);

        return app;
    }
}
