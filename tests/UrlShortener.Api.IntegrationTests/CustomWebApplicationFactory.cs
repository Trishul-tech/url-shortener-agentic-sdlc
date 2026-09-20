using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Net;
using UrlShortener.Application.Abstractions;

namespace UrlShortener.Api.IntegrationTests;

/// <summary>
/// Points the API at a throwaway SQLite file unique to this test run so
/// integration tests never collide with each other or with a developer's
/// local urlshortener.db, and cleans it up on dispose. Also swaps real DNS
/// resolution for a fixed public-IP fake, so SsrfGuard validation (see
/// CreateShortUrlValidator) does not make these tests depend on live
/// network/DNS access - they stay fast, hermetic, and deterministic.
/// SsrfGuard's actual IP-range logic is exercised directly, against real
/// private/public IP literals, in
/// UrlShortener.Application.Tests/SsrfGuardTests.cs.
/// </summary>
public sealed class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"urlshortener-tests-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = $"Data Source={_dbPath}"
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IDnsResolver>();
            services.AddSingleton<IDnsResolver>(new FakePublicDnsResolver());
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }

    private sealed class FakePublicDnsResolver : IDnsResolver
    {
        public Task<IPAddress[]> ResolveAsync(string host, CancellationToken ct) =>
            Task.FromResult(new[] { IPAddress.Parse("93.184.216.34") });
    }
}
