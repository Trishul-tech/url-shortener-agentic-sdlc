using System.Collections.Concurrent;
using System.Text.Json;
using UrlShortener.Api.Orchestration;
using UrlShortener.Orchestrator.Engine;
using UrlShortener.Orchestrator.Governance;
using UrlShortener.Orchestrator.Scenarios;

namespace UrlShortener.Api.Endpoints;

/// <summary>
/// HTTP access to the orchestrator. GET /runs and GET /runs/{scenario} serve the
/// checked-in sample-run artifacts. POST /runs/{scenario} starts a fresh live run
/// of that scenario in-process, reusing the exact Build() + ScenarioRunner.RunAsync()
/// code path the console app uses, but with an HttpApprovalProvider in place of the
/// scripted one, so any approval-gated stage really blocks on a human decision.
/// GET .../live/{runId} polls it (and reports a pending approval, if any), and
/// POST .../live/{runId}/approve|reject resolves it. In-memory run state - prototype
/// scope, resets on process restart. See docs/SPEC.md 2.5.
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
            var approvalProvider = new HttpApprovalProvider();
            var state = new LiveRunState(normalized, outputDir, approvalProvider);
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
                            (engine, context, description) = GreenfieldScenario.Build(approvalProvider);
                            name = "Greenfield";
                            break;
                        case "brownfield":
                            (engine, context, description) = BrownfieldScenario.Build(approvalProvider);
                            name = "Brownfield";
                            break;
                        default:
                            (engine, context, description) = AmbiguousScenario.Build(approvalProvider);
                            name = "Ambiguous";
                            break;
                    }

                    state.Engine = engine;
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
                return Results.Ok(new
                {
                    runId,
                    state.Scenario,
                    state.Status,
                    state.Error,
                    pendingApproval = state.ApprovalProvider.CurrentPending
                });
            }

            var artifacts = ReadScenarioArtifacts(state.OutputDir, state.Scenario);
            return Results.Ok(new { runId, state.Scenario, state.Status, state.FinalPipelineStatus, artifacts });
        });

        group.MapPost("/runs/{scenario}/live/{runId}/approve", (string scenario, string runId, ApprovalDecisionRequest? body) =>
            ResolveApproval(scenario, runId, ApprovalDecision.Approved, body));

        group.MapPost("/runs/{scenario}/live/{runId}/reject", (string scenario, string runId, ApprovalDecisionRequest? body) =>
            ResolveApproval(scenario, runId, ApprovalDecision.Rejected, body));

        group.MapPost("/runs/{scenario}/live/{runId}/revise", (string scenario, string runId, ApprovalDecisionRequest? body) =>
            ResolveApproval(scenario, runId, ApprovalDecision.Deferred, body));

        group.MapGet("/runs/{scenario}/live/{runId}/metrics", (string scenario, string runId) =>
        {
            var normalized = scenario.ToLowerInvariant();
            if (!LiveRuns.TryGetValue(runId, out var state) || state.Scenario != normalized)
            {
                return Results.NotFound();
            }
            if (state.Engine is null)
            {
                return Results.Conflict(new { runId, message = "Run has not started executing yet." });
            }
            return Results.Ok(state.Engine.Metrics.Snapshot());
        });

        group.MapGet("/runs/{scenario}/live/{runId}/audit-trail", (string scenario, string runId) =>
        {
            var normalized = scenario.ToLowerInvariant();
            if (!LiveRuns.TryGetValue(runId, out var state) || state.Scenario != normalized)
            {
                return Results.NotFound();
            }
            if (state.Engine is null)
            {
                return Results.Conflict(new { runId, message = "Run has not started executing yet." });
            }
            return Results.Ok(state.Engine.AuditLog.Events);
        });

        return app;
    }

    private static IResult ResolveApproval(string scenario, string runId, ApprovalDecision decision, ApprovalDecisionRequest? body)
    {
        var normalized = scenario.ToLowerInvariant();
        if (!LiveRuns.TryGetValue(runId, out var state) || state.Scenario != normalized)
        {
            return Results.NotFound();
        }

        var respondedBy = string.IsNullOrWhiteSpace(body?.RespondedBy) ? "http-caller" : body!.RespondedBy!;
        var rationale = string.IsNullOrWhiteSpace(body?.Rationale)
            ? decision switch
            {
                ApprovalDecision.Approved => "Approved via HTTP.",
                ApprovalDecision.Rejected => "Rejected via HTTP.",
                _ => "Revised via HTTP."
            }
            : body!.Rationale!;

        var resolved = state.ApprovalProvider.TryResolve(decision, respondedBy, rationale, body?.Clarifications);
        return resolved
            ? Results.Ok(new { runId, decision = decision.ToString(), resolved = true })
            : Results.Conflict(new { runId, resolved = false, message = "No approval is currently pending for this run." });
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

    private sealed class LiveRunState(string scenario, string outputDir, HttpApprovalProvider approvalProvider)
    {
        public string Scenario { get; } = scenario;
        public string OutputDir { get; } = outputDir;
        public HttpApprovalProvider ApprovalProvider { get; } = approvalProvider;
        public string Status { get; set; } = "Running";
        public string? FinalPipelineStatus { get; set; }
        public string? Error { get; set; }
        public OrchestrationEngine? Engine { get; set; }
    }
}

public sealed record ApprovalDecisionRequest(string? RespondedBy, string? Rationale, IReadOnlyDictionary<string, string>? Clarifications = null);