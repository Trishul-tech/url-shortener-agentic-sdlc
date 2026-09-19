using Microsoft.Extensions.Diagnostics.HealthChecks;
using UrlShortener.Infrastructure.Persistence;

namespace UrlShortener.Api.Middleware;

/// <summary>
/// Minimal, dependency-free health check: confirms the DbContext can open
/// a connection. Kept in-repo instead of a third-party health-check
/// package so the build has one fewer external NuGet dependency to resolve.
/// </summary>
public sealed class SqliteHealthCheck : IHealthCheck
{
    private readonly AppDbContext _db;

    public SqliteHealthCheck(AppDbContext db) => _db = db;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            var canConnect = await _db.Database.CanConnectAsync(ct);
            return canConnect ? HealthCheckResult.Healthy("SQLite reachable.") : HealthCheckResult.Unhealthy("SQLite not reachable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("SQLite health check threw.", ex);
        }
    }
}
