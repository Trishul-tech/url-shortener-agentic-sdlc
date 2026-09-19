using System.Collections.Concurrent;
using UrlShortener.Orchestrator.Graph;

namespace UrlShortener.Orchestrator.Engine;

public sealed record DecisionRecord(
    DateTimeOffset TimestampUtc, StageId Stage, string Decision, string Rationale, string DecidedBy);

/// <summary>
/// Cross-stage state carried through the whole pipeline run: a shared
/// artifact bag (each agent reads what upstream stages produced and writes
/// its own output back) plus an ordered decision-lineage log. This is what
/// lets a downstream stage (e.g. ReleaseReadiness) explain *why* it is
/// making its call by walking the lineage rather than re-deriving context.
/// </summary>
public sealed class PipelineExecutionContext
{
    private readonly ConcurrentDictionary<string, object> _artifacts = new();
    private readonly ConcurrentQueue<DecisionRecord> _lineage = new();

    public string CorrelationId { get; }
    public IReadOnlyDictionary<string, object> Artifacts => _artifacts;
    public IReadOnlyList<DecisionRecord> Lineage => _lineage.ToList();

    public PipelineExecutionContext(string correlationId) => CorrelationId = correlationId;

    public void SetArtifact(string key, object value) => _artifacts[key] = value;

    public T? GetArtifact<T>(string key) => _artifacts.TryGetValue(key, out var v) && v is T typed ? typed : default;

    public bool TryGetArtifact<T>(string key, out T value)
    {
        if (_artifacts.TryGetValue(key, out var v) && v is T typed) { value = typed; return true; }
        value = default!;
        return false;
    }

    public void RecordDecision(StageId stage, string decision, string rationale, string decidedBy) =>
        _lineage.Enqueue(new DecisionRecord(DateTimeOffset.UtcNow, stage, decision, rationale, decidedBy));
}
