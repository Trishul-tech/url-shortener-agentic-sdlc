using System.Collections.Concurrent;
using UrlShortener.Orchestrator.Agents;
using UrlShortener.Orchestrator.Governance;
using UrlShortener.Orchestrator.Graph;
using UrlShortener.Orchestrator.Observability;

namespace UrlShortener.Orchestrator.Engine;

/// <summary>
/// The agentic SDLC orchestration engine. Coordinates a set of per-stage
/// agents over an explicit <see cref="DependencyGraph"/>, executing
/// independent stages concurrently and dependent stages in sequence,
/// enforcing human-approval checkpoints and policy guardrails as entry/exit
/// gates, retrying transient stage failures within a bounded budget, rolling
/// back and re-planning the affected subgraph when a stage exhausts its
/// retries, and safe-stopping the whole run when a blocking guardrail or a
/// rejected approval makes it unsafe to continue. Every decision is written
/// to the <see cref="AuditLog"/> as it happens, so the trail is a byproduct
/// of execution rather than something reconstructed after the fact.
/// </summary>
public sealed class OrchestrationEngine
{
    private readonly DependencyGraph _graph;
    private readonly IReadOnlyDictionary<StageId, IAgent> _agents;
    private readonly IApprovalProvider _approvalProvider;
    private readonly IReadOnlyList<IPolicyGuardrail> _guardrails;
    private readonly OrchestrationOptions _options;
    private readonly MetricsCollector _metrics = new();
    private readonly AuditLog _auditLog;
    private readonly ConcurrentDictionary<StageId, StageRuntimeState> _states;

    private volatile bool _safeStopped;
    private string? _safeStopReason;
    private int _rollbacksUsed;

    public OrchestrationEngine(
        DependencyGraph graph,
        IReadOnlyDictionary<StageId, IAgent> agents,
        IApprovalProvider approvalProvider,
        IReadOnlyList<IPolicyGuardrail> guardrails,
        OrchestrationOptions? options = null,
        string? correlationId = null)
    {
        _graph = graph;
        _agents = agents;
        _approvalProvider = approvalProvider;
        _guardrails = guardrails;
        _options = options ?? new OrchestrationOptions();
        _auditLog = new AuditLog(correlationId ?? $"run-{Guid.NewGuid():N}"[..16]);
        _states = new ConcurrentDictionary<StageId, StageRuntimeState>(
            graph.Nodes.Keys.ToDictionary(id => id, _ => new StageRuntimeState()));
    }

    public async Task<PipelineResult> RunAsync(PipelineExecutionContext context, CancellationToken ct = default)
    {
        _auditLog.Record(AuditEventType.PipelineStarted, null, "engine", "Pipeline run started.");

        using var semaphore = new SemaphoreSlim(_options.MaxDegreeOfParallelism);

        while (!_safeStopped && !ct.IsCancellationRequested)
        {
            var completed = StagesWith(StageStatus.Completed);
            var inFlightOrDone = StagesWith(StageStatus.Running, StageStatus.Completed, StageStatus.FailedTerminal, StageStatus.AwaitingApproval);

            var ready = _graph.ReadyStages(completed, inFlightOrDone);
            if (ready.Count == 0)
                break; // Nothing left that can make progress: either fully done or blocked on AwaitingApproval.

            foreach (var id in ready)
                _states[id].Status = StageStatus.Running;

            var round = ready.Select(id => RunStageWithGateAsync(id, context, semaphore, ct));
            await Task.WhenAll(round);
        }

        var status = DetermineFinalStatus();
        _auditLog.Record(
            status == PipelineStatus.SafeStopped ? AuditEventType.PipelineSafeStopped : AuditEventType.PipelineCompleted,
            null, "engine", $"Pipeline run ended with status {status}.",
            _safeStopReason is null ? null : new Dictionary<string, string> { ["reason"] = _safeStopReason });

        return new PipelineResult(_auditLog.CorrelationId, status, _safeStopReason, _states, context, _auditLog, _metrics.Snapshot());
    }

    private PipelineStatus DetermineFinalStatus()
    {
        if (_safeStopped) return PipelineStatus.SafeStopped;
        if (_states.Values.Any(s => s.Status == StageStatus.FailedTerminal)) return PipelineStatus.Failed;
        if (_states.Values.All(s => s.Status == StageStatus.Completed)) return PipelineStatus.Completed;
        return PipelineStatus.Failed; // graph settled but something never became ready (e.g. blocked on AwaitingApproval)
    }

    private HashSet<StageId> StagesWith(params StageStatus[] statuses) =>
        _states.Where(kv => statuses.Contains(kv.Value.Status)).Select(kv => kv.Key).ToHashSet();

    private async Task RunStageWithGateAsync(
        StageId id, PipelineExecutionContext context, SemaphoreSlim semaphore, CancellationToken ct)
    {
        await semaphore.WaitAsync(ct);
        try
        {
            await ExecuteStageAsync(id, context, ct);
        }
        finally
        {
            semaphore.Release();
        }
    }

    private async Task ExecuteStageAsync(StageId id, PipelineExecutionContext context, CancellationToken ct)
    {
        var node = _graph.Get(id);
        var state = _states[id];
        _metrics.StageStarted(id);
        _auditLog.Record(AuditEventType.StageStarted, id, "engine", $"Stage '{node.DisplayName}' started.");

        while (true)
        {
            state.Attempts++;
            var agent = _agents[id];

            AgentOutcome outcome;
            try
            {
                if (_options.SimulatedWorkDelay > TimeSpan.Zero)
                    await Task.Delay(_options.SimulatedWorkDelay, ct);
                outcome = await agent.ExecuteAsync(context, state.Attempts, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                outcome = AgentOutcome.Fail($"Agent threw an unhandled exception: {ex.Message}");
            }

            var blocked = EvaluateGuardrails(id, context);
            if (blocked is not null)
            {
                state.Status = StageStatus.FailedTerminal;
                state.LastFailureReason = blocked.Message;
                _metrics.StageFailed(id, terminal: true);
                TriggerSafeStop($"Blocking policy guardrail '{blocked.GuardrailName}' on stage {id}: {blocked.Message}");
                return;
            }

            if (outcome.Success)
            {
                if (node.RequiresHumanApproval && !await RequestApprovalAsync(id, node, context, ct))
                {
                    state.Status = StageStatus.FailedTerminal;
                    _metrics.StageFailed(id, terminal: true);
                    return; // Rejection already triggered safe-stop inside RequestApprovalAsync.
                }

                state.Status = StageStatus.Completed;
                state.LastFailureReason = null;
                _metrics.StageSucceeded(id);
                _auditLog.Record(AuditEventType.StageSucceeded, id, "engine", outcome.Summary);
                return;
            }

            state.LastFailureReason = outcome.FailureReason;
            _metrics.StageFailed(id, terminal: false);
            _auditLog.Record(AuditEventType.StageFailed, id, "engine",
                $"Attempt {state.Attempts} failed: {outcome.FailureReason}",
                new Dictionary<string, string> { ["attempt"] = state.Attempts.ToString() });

            if (state.Attempts <= node.MaxRetries)
            {
                _metrics.RetryRecorded();
                _auditLog.Record(AuditEventType.StageRetried, id, "engine",
                    $"Retrying stage (attempt {state.Attempts + 1} of {node.MaxRetries + 1}).");
                continue;
            }

            // Retry budget exhausted: roll back if a target is configured, else fail the pipeline terminally.
            if (_options.RollbackTargets.TryGetValue(id, out var rollbackTarget) && TryRollback(id, rollbackTarget, context))
                return;

            state.Status = StageStatus.FailedTerminal;
            _metrics.StageFailed(id, terminal: true);
            TriggerSafeStop($"Stage {id} retry budget exhausted after {node.MaxRetries + 1} attempts, with no rollback path configured.");
            return;
        }
    }

    private GuardrailFinding? EvaluateGuardrails(StageId id, PipelineExecutionContext context)
    {
        foreach (var guardrail in _guardrails)
        {
            var finding = guardrail.Evaluate(id, context.Artifacts);
            if (finding is null) continue;

            _auditLog.Record(AuditEventType.GuardrailEvaluated, id, guardrail.Name,
                $"[{finding.Severity}] {finding.Message}");

            if (finding.Severity == GuardrailSeverity.Blocking)
            {
                _auditLog.Record(AuditEventType.GuardrailBlocked, id, guardrail.Name, finding.Message);
                return finding;
            }
        }
        return null;
    }

    private async Task<bool> RequestApprovalAsync(StageId id, StageNode node, PipelineExecutionContext context, CancellationToken ct)
    {
        var summary = context.GetArtifact<string>($"{id}:approval_summary") ?? $"Approval requested for stage {id}.";
        var risk = context.GetArtifact<string>($"{id}:risk_level") ?? "Medium";

        var request = new ApprovalRequest(id, summary, risk, ApprovalContextFor(id, context));
        _auditLog.Record(AuditEventType.ApprovalRequested, id, "engine", summary);

        var response = await _approvalProvider.RequestApprovalAsync(request, ct);

        switch (response.Decision)
        {
            case ApprovalDecision.Approved:
                _auditLog.Record(AuditEventType.ApprovalGranted, id, response.RespondedBy, response.Rationale ?? "Approved.");
                if (response.Clarifications is not null)
                    foreach (var kv in response.Clarifications)
                        context.SetArtifact(kv.Key, kv.Value);
                context.RecordDecision(id, "Approved", response.Rationale ?? "Approved.", response.RespondedBy);
                return true;

            case ApprovalDecision.Rejected:
                _auditLog.Record(AuditEventType.ApprovalRejected, id, response.RespondedBy, response.Rationale ?? "Rejected.");
                context.RecordDecision(id, "Rejected", response.Rationale ?? "Rejected.", response.RespondedBy);
                TriggerSafeStop($"Human approval rejected at stage {id}: {response.Rationale}");
                return false;

            default: // Deferred
                _states[id].Status = StageStatus.AwaitingApproval;
                context.RecordDecision(id, "Deferred", response.Rationale ?? "Deferred.", response.RespondedBy);
                return false;
        }
    }

    private static IReadOnlyDictionary<string, string> ApprovalContextFor(StageId id, PipelineExecutionContext context)
    {
        var prefix = $"{id}:ctx:";
        return context.Artifacts
            .Where(kv => kv.Key.StartsWith(prefix, StringComparison.Ordinal))
            .ToDictionary(kv => kv.Key[prefix.Length..], kv => kv.Value?.ToString() ?? string.Empty);
    }

    private bool TryRollback(StageId failedStage, StageId rollbackTarget, PipelineExecutionContext context)
    {
        if (_rollbacksUsed >= _options.MaxRollbacks)
        {
            TriggerSafeStop($"Rollback budget exhausted ({_options.MaxRollbacks}) after stage {failedStage} failed repeatedly.");
            return false;
        }

        _rollbacksUsed++;
        _metrics.RollbackRecorded();

        var affected = _graph.TransitiveDependents(rollbackTarget).Append(rollbackTarget).Distinct().ToList();
        foreach (var stageId in affected)
        {
            _states[stageId] = new StageRuntimeState(); // reset to Pending, attempts cleared for a clean re-run
        }

        _auditLog.Record(AuditEventType.StageRolledBack, failedStage, "engine",
            $"Stage {failedStage} exhausted retries; rolling back to {rollbackTarget}.",
            new Dictionary<string, string> { ["rollbackTarget"] = rollbackTarget.ToString() });

        _metrics.ReplanRecorded();
        _auditLog.Record(AuditEventType.ReplanTriggered, rollbackTarget, "engine",
            $"Re-planning subgraph: {string.Join(", ", affected)} reset to Pending.",
            new Dictionary<string, string> { ["affectedStages"] = string.Join(",", affected) });

        context.RecordDecision(failedStage, "RolledBack",
            $"Retry budget exhausted; rolled back to {rollbackTarget} and re-planned {affected.Count} downstream stage(s).",
            "engine");

        return true;
    }

    private void TriggerSafeStop(string reason)
    {
        if (_safeStopped) return;
        _safeStopped = true;
        _safeStopReason = reason;
        _auditLog.Record(AuditEventType.PipelineSafeStopped, null, "engine", reason);
    }
}
