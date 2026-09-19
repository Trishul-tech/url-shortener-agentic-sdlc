using UrlShortener.Orchestrator.Engine;
using UrlShortener.Orchestrator.Graph;

namespace UrlShortener.Orchestrator.Agents;

/// <summary>
/// Produces (or, for brownfield work, reasons about) the design: components
/// touched, data flow, and key trade-offs. For brownfield scenarios this is
/// where "codebase reasoning" happens - the impacted-modules list drives
/// what Implementation and Testing scope themselves to.
/// </summary>
public sealed class ArchitectureAgent : IAgent
{
    public StageId Stage => StageId.Architecture;

    private readonly string _designSummary;
    private readonly IReadOnlyList<string> _impactedModules;
    private readonly IReadOnlyList<string> _tradeOffs;

    public ArchitectureAgent(string designSummary, IReadOnlyList<string> impactedModules, IReadOnlyList<string> tradeOffs)
    {
        _designSummary = designSummary;
        _impactedModules = impactedModules;
        _tradeOffs = tradeOffs;
    }

    public Task<AgentOutcome> ExecuteAsync(PipelineExecutionContext context, int attempt, CancellationToken ct)
    {
        var clarification = context.GetArtifact<string>("human_clarification");
        var design = clarification is null ? _designSummary : $"{_designSummary} (scoped per clarification: {clarification})";

        context.SetArtifact("design_summary", design);
        context.SetArtifact("impacted_modules", _impactedModules);
        context.SetArtifact("trade_offs", _tradeOffs);
        context.RecordDecision(Stage, "DesignProposed", design, "ArchitectureAgent");

        context.SetArtifact($"{Stage}:approval_summary",
            $"Approve design before implementation. Impacted modules: {string.Join(", ", _impactedModules)}.");
        context.SetArtifact($"{Stage}:risk_level", _impactedModules.Count > 2 ? "High" : "Medium");
        context.SetArtifact($"{Stage}:ctx:design", design);
        context.SetArtifact($"{Stage}:ctx:trade_offs", string.Join(" | ", _tradeOffs));

        return Task.FromResult(AgentOutcome.Ok($"Design proposed touching {_impactedModules.Count} module(s)."));
    }
}
