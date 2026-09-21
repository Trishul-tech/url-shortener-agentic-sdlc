using System.Collections.Concurrent;
using System.Text.Json;
using UrlShortener.Orchestrator.Engine;
using UrlShortener.Orchestrator.Scenarios;

namespace UrlShortener.Api.Endpoints;

/// <summary>
/// HTTP access to the orchestrator. GET /runs and GET /runs/{scenario} serve the
/// checked-in sample-run artifacts (the three required scenarios, run once at
/// authoring time). POST /runs/{scenario} starts a fresh live run of that same
/// scenario in-process, reusing the exact GreenfieldScenario/BrownfieldScenario/
/// AmbiguousScenario.Build() + ScenarioRunner.RunAsync() code path the console
/// app uses, and GET /runs/{scenario}/live/{runId} polls it. Live run state is
/// an in-memory dictionary - prototype scope, resets on process restart, same
/// honest limitation any in-memory run tracker has. See docs/SPEC.md 2.5.
/// </summary>
public static class OrchestratorEndpoints
{
    private static readonly string[] KnownScenarios = { "greenfield", "brownfield", "ambiguous" };
    private static readonly ConcurrentDictionary<string, LiveRunState> LiveRuns = new();

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
            var normalized = scenario.ToLowerInvariant();
            if (!KnownScenarios.Contains(normalized))
            {
                return Results.NotFound();
            }

            var dir = FindSampleRunsDirectory();
            var artifacts = dir is null ? null : ReadScenarioArtifacts(dir, normalized);
            if (artifacts is null)
            {
                return Results.NotFound();
            }

            return Results.Ok(new { scenario = normalized, source = "sample-run", artifacts });
        });

        group.MapPost("/runs/{scenario}", (string scenario) =>
        {
            var normalized = scenario.ToLowerInvariant();
            if (!KnownScenarios.Contains(normalized))
            {
                return Results.NotFound();
            }

            var runId = Guid.NewGuid().ToString("N")[..8];
            var outputDir = Path.Combine(FindOrCreateLiveRunsRoot(), $"{normalized}-{runId}");
            var state = new LiveRunState(normalized, outputDir);
            LiveRuns[runId] = state;

            _ = Task.Run(async () =>
            {
                try
                {
                    string name;
                    OrchestrationEngine engine;
                    PipelineExecutionContext context;
                    string description;

                    switch (normalized)
                    {
                        case "greenfield":
                            (engine, context, description) = GreenfieldScenario.Build();
                            name = "Greenfield";
                            break;
                        case "brownfield":
                            (engine, context, description) = BrownfieldScenario.Build();
                            name = "Brownfield";
                            break;
                        default:
                            (engine, context, description) = AmbiguousScenario.Build();
                            name = "Ambiguous";
                            break;
                    }

                    var report = await ScenarioRunner.RunAsync(name, description, engine, context, outputDir);
                    state.Status = "Completed";
                    state.FinalPipelineStatus = report.Result.Status.ToString();
                }
                catch (Exception ex)
                {
                    state.Status = "Failed";
                    state.Error = ex.Message;
                }
            });

            var pollUrl = $"/api/v1/orchestrator/runs/{normalized}/live/{runId}";
            return Results.Accepted(pollUrl, new { runId, scenario = normalized, status = "Accepted", pollAt = pollUrl });
        });

        group.MapGet("/runs/{scenario}/live/{runId}", (string scenario, string runId) =>
        {
            var normalized = scenario.ToLowerInvariant();
            if (!LiveRuns.TryGetValue(runId, out var state) || state.Scenario != normalized)
            {
                return Results.NotFound();
            }

            if (state.Status != "Completed")
            {
                return Results.Ok(new { runId, state.Scenario, state.Status, state.Error });
            }

            var artifacts = ReadScenarioArtifacts(state.OutputDir, state.Scenario);
            return Results.Ok(new { runId, state.Scenario, state.Status, state.FinalPipelineStatus, artifacts });
        });

        return app;
    }

    private static object? ReadScenarioArtifacts(string dir, string slug)
    {
        var metricsPath = Path.Combine(dir, $"{slug}.metrics.json");
        if (!File.Exists(metricsPath))
        {
            return null;
        }

        var stagesPath = Path.Combine(dir, $"{slug}.stage-states.json");
        var decisionsPath = Path.Combine(dir, $"{slug}.decision-lineage.json");

        return new
        {
            metrics = JsonDocument.Parse(File.ReadAllText(metricsPath)).RootElement,
            stageStates = File.Exists(stagesPath)
                ? JsonDocument.Parse(File.ReadAllText(stagesPath)).RootElement
                : (JsonElement?)null,
            decisionLineage = File.Exists(decisionsPath)
                ? JsonDocument.Parse(File.ReadAllText(decisionsPath)).RootElement
                : (JsonElement?)null
        };
    }

    private static string FindOrCreateLiveRunsRoot()
    {
        var sampleRunsDir = FindSampleRunsDirectory();
        var root = sampleRunsDir is not null
            ? Path.Combine(Directory.GetParent(sampleRunsDir)!.FullName, "live-runs")
            : Path.Combine(AppContext.BaseDirectory, "artifacts", "live-runs");

        Directory.CreateDirectory(root);
        return root;
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

    private sealed class LiveRunState(string scenario, string outputDir)
    {
        public string Scenario { get; } = scenario;
        public string OutputDir { get; } = outputDir;
        public string Status { get; set; } = "Running";
        public string? FinalPipelineStatus { get; set; }
        public string? Error { get; set; }
    }
}