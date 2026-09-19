using UrlShortener.Orchestrator.Engine;
using UrlShortener.Orchestrator.Graph;

namespace UrlShortener.Orchestrator.Agents;

/// <summary>
/// Simulates writing the code change: emits the set of files touched and
/// any policy-relevant flags (e.g. whether the change proposes handling
/// PII) so downstream guardrails and reviewers have something concrete to
/// evaluate. Supports scripted failure injection so scenarios can
/// demonstrate the retry/rollback path deterministically.
/// </summary>
public sealed class ImplementationAgent : IAgent
{
    public StageId Stage => StageId.Implementation;

    private readonly IReadOnlyList<string> _filesOnSuccess;
    private readonly bool _exposesRawIp;
    private readonly Func<int, bool> _shouldFail;
    private readonly string _failureReason;

    /// <summary>
    /// Total number of times this agent has actually executed, across the
    /// whole pipeline run - including re-executions after a rollback (unlike
    /// the per-attempt counter the engine passes in, which resets when the
    /// stage's runtime state is reset). Scenarios use this to model "the
    /// rework after rollback fixes the bug" deterministically.
    /// </summary>
    public int ExecutionCount { get; private set; }

    public ImplementationAgent(
        IReadOnlyList<string> filesOnSuccess,
        bool exposesRawIp = false,
        Func<int, bool>? shouldFail = null,
        string failureReason = "Build failed: unresolved regression in modified module.")
    {
        _filesOnSuccess = filesOnSuccess;
        _exposesRawIp = exposesRawIp;
        _shouldFail = shouldFail ?? (_ => false);
        _failureReason = failureReason;
    }

    public Task<AgentOutcome> ExecuteAsync(PipelineExecutionContext context, int attempt, CancellationToken ct)
    {
        ExecutionCount++;

        if (_shouldFail(attempt))
        {
            context.RecordDecision(Stage, "ImplementationFailed", _failureReason, "ImplementationAgent");
            return Task.FromResult(AgentOutcome.Fail(_failureReason));
        }

        context.SetArtifact("code_artifacts", _filesOnSuccess);
        context.SetArtifact("exposes_raw_ip", _exposesRawIp);
        context.RecordDecision(Stage, "Implemented", $"Touched files: {string.Join(", ", _filesOnSuccess)}", "ImplementationAgent");

        return Task.FromResult(AgentOutcome.Ok($"Implemented change across {_filesOnSuccess.Count} file(s)."));
    }
}
