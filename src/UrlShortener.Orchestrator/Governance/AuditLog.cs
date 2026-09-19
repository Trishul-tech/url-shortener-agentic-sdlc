using System.Text.Json;
using System.Text.Json.Serialization;
using UrlShortener.Orchestrator.Graph;

namespace UrlShortener.Orchestrator.Governance;

public enum AuditEventType
{
    PipelineStarted, PipelineCompleted, PipelineSafeStopped,
    StageEntryGateEvaluated, StageStarted, StageRetried, StageSucceeded, StageFailed,
    ApprovalRequested, ApprovalGranted, ApprovalRejected,
    GuardrailEvaluated, GuardrailBlocked,
    StageRolledBack, ReplanTriggered
}

public sealed record AuditEvent(
    string CorrelationId,
    DateTimeOffset TimestampUtc,
    AuditEventType EventType,
    StageId? Stage,
    string Actor,
    string Summary,
    IReadOnlyDictionary<string, string>? Data = null);

/// <summary>
/// Append-only, timestamped audit trail with a stable correlation id per
/// pipeline run. This is the "audit-grade observability and traceability"
/// requirement and doubles as the decision-lineage record: every gate
/// evaluation, approval, retry, and rollback is captured with its rationale.
/// </summary>
public sealed class AuditLog
{
    private static readonly JsonSerializerOptions JsonOptions = new() { Converters = { new JsonStringEnumConverter() } };

    private readonly List<AuditEvent> _events = new();
    public string CorrelationId { get; }

    public AuditLog(string correlationId) => CorrelationId = correlationId;

    public IReadOnlyList<AuditEvent> Events => _events;

    public void Record(AuditEventType type, StageId? stage, string actor, string summary,
        IReadOnlyDictionary<string, string>? data = null)
    {
        _events.Add(new AuditEvent(CorrelationId, DateTimeOffset.UtcNow, type, stage, actor, summary, data));
    }

    public string ToJsonLines() =>
        string.Join(Environment.NewLine, _events.Select(e => JsonSerializer.Serialize(e, JsonOptions)));
}
