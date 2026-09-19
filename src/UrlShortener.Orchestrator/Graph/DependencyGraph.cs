namespace UrlShortener.Orchestrator.Graph;

/// <summary>
/// An explicit dependency graph over SDLC stages. Validates on construction
/// that it is acyclic (Kahn's algorithm) so the engine never has to defend
/// against a graph that can deadlock.
/// </summary>
public sealed class DependencyGraph
{
    private readonly Dictionary<StageId, StageNode> _nodes;

    public IReadOnlyDictionary<StageId, StageNode> Nodes => _nodes;

    public DependencyGraph(IEnumerable<StageNode> nodes)
    {
        _nodes = nodes.ToDictionary(n => n.Id);
        ValidateAcyclic();
    }

    public StageNode Get(StageId id) => _nodes[id];

    /// <summary>Direct dependents of a stage - used to compute the blast radius of a re-plan.</summary>
    public IEnumerable<StageId> Dependents(StageId id) =>
        _nodes.Values.Where(n => n.DependsOn.Contains(id)).Select(n => n.Id);

    /// <summary>All transitive dependents of a stage, in no particular order.</summary>
    public IReadOnlySet<StageId> TransitiveDependents(StageId id)
    {
        var result = new HashSet<StageId>();
        var queue = new Queue<StageId>(Dependents(id));
        while (queue.Count > 0)
        {
            var next = queue.Dequeue();
            if (!result.Add(next)) continue;
            foreach (var d in Dependents(next)) queue.Enqueue(d);
        }
        return result;
    }

    /// <summary>
    /// Stages whose dependencies are all in <paramref name="completed"/> and
    /// which are not themselves already completed/terminal. Multiple entries
    /// returned here are the engine's "parallel path" - they have no
    /// ordering constraint between them.
    /// </summary>
    public IReadOnlyList<StageId> ReadyStages(ISet<StageId> completed, ISet<StageId> inFlightOrDone) =>
        _nodes.Values
            .Where(n => !inFlightOrDone.Contains(n.Id))
            .Where(n => n.DependsOn.All(completed.Contains))
            .Select(n => n.Id)
            .ToList();

    private void ValidateAcyclic()
    {
        var inDegree = _nodes.Keys.ToDictionary(id => id, _ => 0);
        foreach (var node in _nodes.Values)
            foreach (var dep in node.DependsOn)
                inDegree[node.Id]++;

        var queue = new Queue<StageId>(inDegree.Where(kv => kv.Value == 0).Select(kv => kv.Key));
        var visited = 0;
        var remaining = new Dictionary<StageId, int>(inDegree);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            visited++;
            foreach (var dependent in Dependents(current))
            {
                remaining[dependent]--;
                if (remaining[dependent] == 0) queue.Enqueue(dependent);
            }
        }

        if (visited != _nodes.Count)
            throw new InvalidOperationException("Dependency graph contains a cycle - orchestration graphs must be a DAG.");
    }
}
