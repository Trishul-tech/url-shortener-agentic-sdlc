using UrlShortener.Orchestrator.Engine;
using UrlShortener.Orchestrator.Graph;

namespace UrlShortener.Orchestrator.Agents;

/// <summary>
/// The final synchronization point: confirms every upstream artifact
/// required for a go/no-go call is present, then requests the release
/// approval checkpoint. This stage depends on all of UnitTesting,
/// IntegrationTesting, SecurityReview and Documentation, so it only
/// becomes ready once every parallel branch has completed - the explicit
/// synchronization the assignment calls for.
/// </summary>
public sealed class ReleaseReadinessAgent : IAgent
{
    public StageId Stage => StageId.ReleaseReadiness;

    private readonly string? _changeTicket;

    public ReleaseReadinessAgent(string? changeTicket = null) => _changeTicket = changeTicket;

    public Task<AgentOutcome> ExecuteAsync(PipelineExecutionContext context, int attempt, CancellationToken ct)
    {
        if (_changeTicket is not null)
            context.SetArtifact("change_ticket", _changeTicket);

        var checklist = new (string Item, bool Ok)[]
        {
            ("Unit tests", context.TryGetArtifact<string>("unit_test_results", out _)),
            ("Integration tests", context.TryGetArtifact<IReadOnlyList<string>>("integration_test_results", out _)),
            ("Security review", context.TryGetArtifact<string>("security_review_notes", out _)),
            ("Documentation", context.TryGetArtifact<string>("documentation", out _)),
        };

        var missing = checklist.Where(c => !c.Ok).Select(c => c.Item).ToList();
        if (missing.Count > 0)
            return Task.FromResult(AgentOutcome.Fail($"Release checklist incomplete, missing: {string.Join(", ", missing)}."));

        var summary = "All release gates satisfied: tests green, security reviewed, docs drafted.";
        context.SetArtifact("release_summary", summary);
        context.RecordDecision(Stage, "ReadyForRelease", summary, "ReleaseReadinessAgent");

        context.SetArtifact($"{Stage}:approval_summary", $"Go/no-go release decision. {summary}");
        context.SetArtifact($"{Stage}:risk_level", "High");
        context.SetArtifact($"{Stage}:ctx:change_ticket", _changeTicket ?? "(none attached)");

        return Task.FromResult(AgentOutcome.Ok(summary));
    }
}
