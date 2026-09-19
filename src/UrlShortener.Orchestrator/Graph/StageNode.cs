namespace UrlShortener.Orchestrator.Graph;

/// <summary>
/// A node in the SDLC dependency graph. Gates are predicates evaluated by
/// the engine around agent execution; keeping them as data on the node
/// (rather than hardcoded in the engine) is what lets each scenario
/// configure different governance requirements for the same graph shape.
/// </summary>
public sealed record StageNode(
    StageId Id,
    string DisplayName,
    IReadOnlyList<StageId> DependsOn,
    bool RequiresHumanApproval = false,
    int MaxRetries = 2,
    bool CriticalPath = true)
{
    public static StageNode Root(StageId id, string name, bool requiresApproval = false, int maxRetries = 2) =>
        new(id, name, Array.Empty<StageId>(), requiresApproval, maxRetries);
}
