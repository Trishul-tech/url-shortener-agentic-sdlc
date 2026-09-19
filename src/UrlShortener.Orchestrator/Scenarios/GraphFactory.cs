using UrlShortener.Orchestrator.Graph;

namespace UrlShortener.Orchestrator.Scenarios;

/// <summary>
/// Builds the standard SDLC dependency graph used by all three scenarios.
/// The shape is fixed (it's the orchestration model being demonstrated);
/// what differs per scenario is the agents, guardrails, and approval script
/// wired to it.
/// </summary>
public static class GraphFactory
{
    public static DependencyGraph BuildStandardSdlcGraph(int implementationMaxRetries = 1, int testingMaxRetries = 1)
    {
        var nodes = new List<StageNode>
        {
            StageNode.Root(StageId.Requirements, "Requirements Understanding", requiresApproval: true, maxRetries: 0),

            new(StageId.Architecture, "Architecture & Design",
                DependsOn: new[] { StageId.Requirements }, RequiresHumanApproval: true, MaxRetries: 0),

            new(StageId.Implementation, "Implementation",
                DependsOn: new[] { StageId.Architecture }, RequiresHumanApproval: false, MaxRetries: implementationMaxRetries),

            // Testing, Documentation and SecurityReview all depend only on Implementation:
            // no ordering constraint between them -> the engine runs them in parallel.
            new(StageId.UnitTesting, "Unit Testing",
                DependsOn: new[] { StageId.Implementation }, RequiresHumanApproval: false, MaxRetries: testingMaxRetries),

            new(StageId.IntegrationTesting, "Integration Testing",
                DependsOn: new[] { StageId.Implementation }, RequiresHumanApproval: false, MaxRetries: testingMaxRetries),

            new(StageId.SecurityReview, "Security & Compliance Review",
                DependsOn: new[] { StageId.Implementation }, RequiresHumanApproval: false, MaxRetries: 0),

            new(StageId.Documentation, "Documentation",
                DependsOn: new[] { StageId.Implementation }, RequiresHumanApproval: false, MaxRetries: 1),

            // ReleaseReadiness synchronizes all four parallel branches.
            new(StageId.ReleaseReadiness, "Release Readiness",
                DependsOn: new[] { StageId.UnitTesting, StageId.IntegrationTesting, StageId.SecurityReview, StageId.Documentation },
                RequiresHumanApproval: true, MaxRetries: 0),
        };

        return new DependencyGraph(nodes);
    }
}
