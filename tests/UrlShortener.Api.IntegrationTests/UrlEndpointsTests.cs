using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using UrlShortener.Api.Contracts;
using Xunit;

namespace UrlShortener.Api.IntegrationTests;

/// <summary>
/// End-to-end tests against an in-memory TestServer running the real
/// Program.cs pipeline (SQLite file per test run via WebApplicationFactory
/// - see CustomWebApplicationFactory for the isolated-database setup).
/// </summary>
public class UrlEndpointsTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public UrlEndpointsTests(CustomWebApplicationFactory factory) =>
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task CreateShortUrl_ThenRedirect_ReturnsOriginalTarget()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/v1/urls",
            new CreateShortUrlRequest("https://example.com/some/deep/page", null, null, null));

        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<CreateShortUrlResponse>();
        created.Should().NotBeNull();

        var redirectResponse = await _client.GetAsync($"/{created!.Code}");

        redirectResponse.StatusCode.Should().Be(HttpStatusCode.Found);
        redirectResponse.Headers.Location!.ToString().Should().Be("https://example.com/some/deep/page");
    }

    [Fact]
    public async Task CreateShortUrl_WithCustomAlias_UsesThatAlias()
    {
        var alias = $"myalias{Guid.NewGuid():N}"[..20];
        var response = await _client.PostAsJsonAsync("/api/v1/urls",
            new CreateShortUrlRequest("https://example.com", alias, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<CreateShortUrlResponse>();
        created!.Code.Should().Be(alias);
    }

    [Fact]
    public async Task CreateShortUrl_WithDuplicateAlias_ReturnsConflict()
    {
        var alias = $"dup{Guid.NewGuid():N}"[..15];
        await _client.PostAsJsonAsync("/api/v1/urls", new CreateShortUrlRequest("https://example.com/one", alias, null, null));

        var second = await _client.PostAsJsonAsync("/api/v1/urls", new CreateShortUrlRequest("https://example.com/two", alias, null, null));

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task CreateShortUrl_WithInvalidUrl_ReturnsBadRequest()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/urls", new CreateShortUrlRequest("not-a-url", null, null, null));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Redirect_ForUnknownCode_ReturnsNotFound()
    {
        var response = await _client.GetAsync("/doesnotexist");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Deactivate_ThenRedirect_ReturnsGone()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/v1/urls", new CreateShortUrlRequest("https://example.com/x", null, null, null));
        var created = await createResponse.Content.ReadFromJsonAsync<CreateShortUrlResponse>();

        var deleteResponse = await _client.DeleteAsync($"/api/v1/urls/{created!.Code}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var redirectResponse = await _client.GetAsync($"/{created.Code}");
        redirectResponse.StatusCode.Should().Be(HttpStatusCode.Gone);
    }

    [Fact]
    public async Task Analytics_AfterRedirects_ReflectsClickCount()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/v1/urls", new CreateShortUrlRequest("https://example.com/analytics-target", null, null, null));
        var created = await createResponse.Content.ReadFromJsonAsync<CreateShortUrlResponse>();

        await _client.GetAsync($"/{created!.Code}");
        await _client.GetAsync($"/{created.Code}");
        await _client.GetAsync($"/{created.Code}");

        var analyticsResponse = await _client.GetAsync($"/api/v1/urls/{created.Code}/analytics");
        analyticsResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var analytics = await analyticsResponse.Content.ReadFromJsonAsync<AnalyticsResponse>();
        analytics!.TotalClicks.Should().Be(3);
    }

    [Fact]
    public async Task Health_ReturnsHealthy()
    {
        var response = await _client.GetAsync("/health");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
