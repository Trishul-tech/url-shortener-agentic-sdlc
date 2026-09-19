using UrlShortener.Orchestrator.Engine;
using UrlShortener.Orchestrator.Graph;

namespace UrlShortener.Orchestrator.Agents;

/// <summary>Produces the supporting documentation artifact for the change (API docs, README delta, changelog entry).</summary>
public sealed class DocumentationAgent : IAgent
{
    public StageId Stage => StageId.Documentation;

    private readonly string _docSummary;

    public DocumentationAgent(string docSummary) => _docSummary = docSummary;

    public Task<AgentOutcome> ExecuteAsync(PipelineExecutionContext context, int attempt, CancellationToken ct)
    {
        context.SetArtifact("documentation", _docSummary);
        context.RecordDecision(Stage, "DocumentationDrafted", _docSummary, "DocumentationAgent");
        return Task.FromResult(AgentOutcome.Ok("Documentation drafted."));
    }
}
