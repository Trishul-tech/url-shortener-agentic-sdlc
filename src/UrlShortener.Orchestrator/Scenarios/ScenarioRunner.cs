using System.Text.Json;
using UrlShortener.Orchestrator.Engine;

namespace UrlShortener.Orchestrator.Scenarios;

public sealed record ScenarioReport(string Name, string Description, PipelineResult Result);

/// <summary>Runs a scenario's engine and writes its audit trail + metrics to disk for review.</summary>
public static class ScenarioRunner
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static async Task<ScenarioReport> RunAsync(
        string name, string description, OrchestrationEngine engine, PipelineExecutionContext context, string outputDir)
    {
        Console.WriteLine($"=== Scenario: {name} ===");
        Console.WriteLine(description);
        Console.WriteLine();

        var result = await engine.RunAsync(context);

        Directory.CreateDirectory(outputDir);
        var slug = name.ToLowerInvariant().Replace(' ', '-');

        await File.WriteAllTextAsync(Path.Combine(outputDir, $"{slug}.audit.jsonl"), result.AuditLog.ToJsonLines());
        await File.WriteAllTextAsync(Path.Combine(outputDir, $"{slug}.metrics.json"), JsonSerializer.Serialize(result.Metrics, JsonOptions));

        var lineage = context.Lineage.Select(d => new
        {
            d.TimestampUtc,
            Stage = d.Stage.ToString(),
            d.Decision,
            d.Rationale,
            d.DecidedBy
        });
        await File.WriteAllTextAsync(Path.Combine(outputDir, $"{slug}.decision-lineage.json"), JsonSerializer.Serialize(lineage, JsonOptions));

        var stageStates = result.FinalStageStates.ToDictionary(
            kv => kv.Key.ToString(),
            kv => new { Status = kv.Value.Status.ToString(), kv.Value.Attempts, kv.Value.LastFailureReason });
        await File.WriteAllTextAsync(Path.Combine(outputDir, $"{slug}.stage-states.json"), JsonSerializer.Serialize(stageStates, JsonOptions));

        Console.WriteLine($"Pipeline status: {result.Status}" + (result.SafeStopReason is null ? "" : $" ({result.SafeStopReason})"));
        Console.WriteLine($"Metrics: successRate={result.Metrics.SuccessRate:P0} retries={result.Metrics.RetryCount} " +
                           $"rollbacks={result.Metrics.RollbackCount} replans={result.Metrics.ReplanCount} " +
                           $"mttrMs={result.Metrics.MeanTimeToRecoveryMs?.ToString("F0") ?? "n/a"} latencyMs={result.Metrics.TotalLatencyMs:F0}");
        Console.WriteLine($"Artifacts written to {outputDir} (prefix '{slug}.*')");
        Console.WriteLine();

        return new ScenarioReport(name, description, result);
    }
}
