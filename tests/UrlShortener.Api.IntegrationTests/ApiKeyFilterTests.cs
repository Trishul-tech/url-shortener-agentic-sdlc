using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UrlShortener.Api.Middleware;
using Xunit;

namespace UrlShortener.Api.IntegrationTests;

public class ApiKeyFilterTests
{
    private static EndpointFilterInvocationContext BuildContext(string? apiKeyConfig, string? providedHeader)
    {
        var configData = new Dictionary<string, string?>();
        if (apiKeyConfig is not null)
            configData["ApiKey"] = apiKeyConfig;

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(configData).Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        var provider = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext { RequestServices = provider };
        if (providedHeader is not null)
            httpContext.Request.Headers["X-Api-Key"] = providedHeader;

        return EndpointFilterInvocationContext.Create(httpContext);
    }

    [Fact]
    public async Task InvokeAsync_NoApiKeyConfigured_AllowsRequestThrough()
    {
        var context = BuildContext(apiKeyConfig: null, providedHeader: null);
        var filter = new ApiKeyFilter();

        var result = await filter.InvokeAsync(context, _ => ValueTask.FromResult<object?>("ok"));

        Assert.Equal("ok", result);
    }

    [Fact]
    public async Task InvokeAsync_ApiKeyConfigured_MissingHeader_ReturnsUnauthorized()
    {
        var context = BuildContext(apiKeyConfig: "secret123", providedHeader: null);
        var filter = new ApiKeyFilter();

        var result = await filter.InvokeAsync(context, _ => ValueTask.FromResult<object?>("ok"));

        var problem = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, problem.StatusCode!.Value);
    }

    [Fact]
    public async Task InvokeAsync_ApiKeyConfigured_CorrectHeader_AllowsRequestThrough()
    {
        var context = BuildContext(apiKeyConfig: "secret123", providedHeader: "secret123");
        var filter = new ApiKeyFilter();

        var result = await filter.InvokeAsync(context, _ => ValueTask.FromResult<object?>("ok"));

        Assert.Equal("ok", result);
    }

    [Fact]
    public async Task InvokeAsync_ApiKeyConfigured_WrongHeader_ReturnsUnauthorized()
    {
        var context = BuildContext(apiKeyConfig: "secret123", providedHeader: "wrong");
        var filter = new ApiKeyFilter();

        var result = await filter.InvokeAsync(context, _ => ValueTask.FromResult<object?>("ok"));

        var problem = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, problem.StatusCode!.Value);
    }
}
