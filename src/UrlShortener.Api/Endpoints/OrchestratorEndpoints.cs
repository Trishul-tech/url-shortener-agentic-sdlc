using System.Text.Json;

namespace UrlShortener.Api.Endpoints;

/// <summary>
/// Read-only HTTP access to the orchestrator's sample-run artifacts
/// (written by MetricsCollector / AuditLog / DecisionLineage under
/// artifacts/sample-runs/). This does not drive a live orchestration run;
/// it exposes what the three required scenarios already produced, so a
/// caller can inspect run results over HTTP instead of reading files by
/// hand. See docs/SPEC.md section 3.4 for what each artifact contains.
/// </summary>
public static class OrchestratorEndpoints
{
    private static readonly string[] KnownScenarios = { "greenfield", "brownfield", "ambiguous" };

    public static IEndpointRouteBuilder MapOrchestratorEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/orchestrator").WithTags("Orchestrator");

        group.MapGet("/runs", () =>
        {
            var dir = FindSampleRunsDirectory();
            if (dir is null)
            {
                return Results.Ok(Array.Empty<string>());
            }

            var available = KnownScenarios
                .Where(s => File.Exists(Path.Combine(dir, $"{s}.metrics.json")))
                .ToArray();

            return Results.Ok(available);
        });

        group.MapGet("/runs/{scenario}", (string scenario) =>
        {
            if (!KnownScenarios.Contains(scenario, StringComparer.OrdinalIgnoreCase))
            {
                return Results.NotFound();
            }

            var dir = FindSampleRunsDirectory();
            var metricsPath = dir is null ? null : Path.Combine(dir, $"{scenario}.metrics.json");

            if (metricsPath is null || !File.Exists(metricsPath))
            {
                return Results.NotFound();
            }

            var stagesPath = Path.Combine(dir!, $"{scenario}.stage-states.json");
            var decisionsPath = Path.Combine(dir!, $"{scenario}.decision-lineage.json");

            var response = new
            {
                scenario,
                metrics = JsonDocument.Parse(File.ReadAllText(metricsPath)).RootElement,
                stageStates = File.Exists(stagesPath)
                    ? JsonDocument.Parse(File.ReadAllText(stagesPath)).RootElement
                    : (JsonElement?)null,
                decisionLineage = File.Exists(decisionsPath)
                    ? JsonDocument.Parse(File.ReadAllText(decisionsPath)).RootElement
                    : (JsonElement?)null
            };

            return Results.Ok(response);
        });

        return app;
    }

    private static string? FindSampleRunsDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir.FullName, "artifacts", "sample-runs");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }
        return null;
    }
}