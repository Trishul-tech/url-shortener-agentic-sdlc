using UrlShortener.Orchestrator.Governance;
using UrlShortener.Orchestrator.Graph;
using UrlShortener.Orchestrator.Observability;

namespace UrlShortener.Orchestrator.Engine;

public enum PipelineStatus { Completed, SafeStopped, Failed }

public sealed record PipelineResult(
    string CorrelationId,
    PipelineStatus Status,
    string? SafeStopReason,
    IReadOnlyDictionary<StageId, StageRuntimeState> FinalStageStates,
    PipelineExecutionContext Context,
    AuditLog AuditLog,
    RunMetrics Metrics);
