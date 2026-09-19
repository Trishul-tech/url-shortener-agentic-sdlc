using UrlShortener.Orchestrator.Graph;

namespace UrlShortener.Orchestrator.Engine;

/// <summary>
/// Governance and reliability knobs for a pipeline run. Separating this
/// from the graph shape means the same DAG can be run with different risk
/// postures (e.g. stricter rollback budgets in production vs. relaxed
/// budgets in a sandbox demo) without redefining stages.
/// </summary>
public sealed class OrchestrationOptions
{
    /// <summary>If a stage in this map exhausts its retries, the engine rolls back to the mapped stage instead of failing terminally.</summary>
    public IReadOnlyDictionary<StageId, StageId> RollbackTargets { get; init; } = new Dictionary<StageId, StageId>();

    /// <summary>Bounds total rollbacks for the whole run, preventing an oscillating (retry -> rollback -> retry) loop from running forever.</summary>
    public int MaxRollbacks { get; init; } = 1;

    public int MaxDegreeOfParallelism { get; init; } = 4;

    /// <summary>Simulated per-attempt delay, so the demo's MTTR/latency metrics are non-zero and legible; set to TimeSpan.Zero in tests.</summary>
    public TimeSpan SimulatedWorkDelay { get; init; } = TimeSpan.FromMilliseconds(30);
}
