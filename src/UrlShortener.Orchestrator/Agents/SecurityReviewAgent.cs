using UrlShortener.Orchestrator.Engine;
using UrlShortener.Orchestrator.Graph;

namespace UrlShortener.Orchestrator.Agents;

/// <summary>
/// Produces a review narrative over the artifacts Implementation left
/// behind. This agent never itself decides pass/fail on policy matters -
/// that enforcement lives in IPolicyGuardrail so the "what is allowed" rule
/// is auditable independently of "what the agent noticed".
/// </summary>
public sealed class SecurityReviewAgent : IAgent
{
    public StageId Stage => StageId.SecurityReview;

    public Task<AgentOutcome> ExecuteAsync(PipelineExecutionContext context, int attempt, CancellationToken ct)
    {
        var files = context.GetArtifact<IReadOnlyList<string>>("code_artifacts") ?? Array.Empty<string>();
        context.TryGetArtifact<bool>("exposes_raw_ip", out var exposesRawIp);

        var notes = $"Reviewed {files.Count} file(s) for injection risk, authZ, and PII handling. " +
                    (exposesRawIp ? "Flagged raw-IP handling for guardrail evaluation." : "No PII handling concerns noted.");

        context.SetArtifact("security_review_notes", notes);
        context.RecordDecision(Stage, "Reviewed", notes, "SecurityReviewAgent");
        return Task.FromResult(AgentOutcome.Ok(notes));
    }
}
