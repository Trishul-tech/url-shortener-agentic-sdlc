namespace UrlShortener.Api.Endpoints;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapAppHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapHealthChecks("/health").WithTags("Health").ExcludeFromDescription();
        return app;
    }
}
