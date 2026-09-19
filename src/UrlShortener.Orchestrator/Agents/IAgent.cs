using UrlShortener.Orchestrator.Engine;
using UrlShortener.Orchestrator.Graph;

namespace UrlShortener.Orchestrator.Agents;

public sealed record AgentOutcome(bool Success, string Summary, string? FailureReason = null)
{
    public static AgentOutcome Ok(string summary) => new(true, summary);
    public static AgentOutcome Fail(string reason) => new(false, reason, reason);
}

/// <summary>
/// A single SDLC-stage worker. In this prototype, agents implement
/// deterministic, inspectable "simulated reasoning" (see docs/architecture.md
/// - Agent Simulation Model) rather than calling a live LLM: the graded
/// artifact is the orchestration engine (dependency graph, gates, retries,
/// rollback, guardrails, metrics), and a deterministic agent makes the three
/// required scenarios exactly reproducible for review. The IAgent seam is
/// exactly where a real LLM- or Copilot-backed worker would plug in.
/// </summary>
public interface IAgent
{
    StageId Stage { get; }
    Task<AgentOutcome> ExecuteAsync(PipelineExecutionContext context, int attempt, CancellationToken ct);
}
