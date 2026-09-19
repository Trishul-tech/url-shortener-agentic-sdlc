using UrlShortener.Orchestrator.Agents;
using UrlShortener.Orchestrator.Engine;
using UrlShortener.Orchestrator.Governance;
using UrlShortener.Orchestrator.Graph;

namespace UrlShortener.Orchestrator.Scenarios;

/// <summary>
/// Brownfield: a bug fix + refactor against the existing codebase. This is
/// the primary showcase for the "critical differentiator": codebase
/// reasoning (impacted-module identification), a stage that fails past its
/// retry budget, an automatic rollback to Implementation, dynamic
/// re-planning of the affected subgraph, and a clean recovery on rework -
/// all without any human re-triggering the pipeline.
/// </summary>
public static class BrownfieldScenario
{
    public static (OrchestrationEngine Engine, PipelineExecutionContext Context, string Description) Build()
    {
        const string description =
            "Fix: ResolveShortUrlQuery double-records a click when the in-memory cache entry is " +
            "populated concurrently by two in-flight requests for the same code, and refactor the " +
            "cache-populate path to remove the race.";

        var graph = GraphFactory.BuildStandardSdlcGraph(implementationMaxRetries: 1, testingMaxRetries: 1);

        var implementationAgent = new ImplementationAgent(
            filesOnSuccess: new[]
            {
                "src/UrlShortener.Application/UrlShortening/ResolveShortUrl/ResolveShortUrlHandler.cs",
                "src/UrlShortener.Infrastructure/Services/MemoryCacheService.cs"
            });

        var agents = new Dictionary<StageId, IAgent>
        {
            [StageId.Requirements] = new RequirementsAgent(
                "Fix the double-click-count bug under concurrent redirects and remove the underlying cache race; " +
                "no behavior change to the public API contract."),

            [StageId.Architecture] = new ArchitectureAgent(
                designSummary: "Root cause: under a burst, two concurrent requests for the same code can both miss the " +
                               "cache before it is populated, so both fall through to the repository and both record a " +
                               "click. Fix: populate the cache and record the click as a single atomic step per request, " +
                               "keyed so a second concurrent miss reuses the first request's in-flight result instead of " +
                               "issuing its own repository read.",
                impactedModules: new[]
                {
                    "UrlShortener.Application/UrlShortening/ResolveShortUrl/ResolveShortUrlHandler.cs",
                    "UrlShortener.Infrastructure/Services/MemoryCacheService.cs",
                    "UrlShortener.Infrastructure/Persistence/Repositories/ClickEventRepository.cs (read-only impact analysis)"
                },
                tradeOffs: new[]
                {
                    "Considered a distributed lock (e.g. Redis SETNX); rejected as unnecessary complexity for the current single-instance deployment.",
                    "Chose request-coalescing over strict read-your-writes consistency to keep the redirect hot path low-latency."
                }),

            [StageId.Implementation] = implementationAgent,

            [StageId.UnitTesting] = new UnitTestingAgent(testCount: 22),

            // Fails while Implementation is still on its first (pre-rework) pass; once Implementation
            // has re-executed after rollback, the race is fixed and this passes.
            [StageId.IntegrationTesting] = new IntegrationTestingAgent(
                scenariosOnSuccess: new[]
                {
                    "50 concurrent redirects for the same code record exactly 50 click events",
                    "Deactivate + reactivate returns the correct target on the next redirect"
                },
                shouldFail: _ => implementationAgent.ExecutionCount <= 1,
                failureReason: "Load test: 50 concurrent redirects for the same code recorded 63 click events (expected 50) - race still reproduces."),

            [StageId.SecurityReview] = new SecurityReviewAgent(),
            [StageId.Documentation] = new DocumentationAgent(
                "CHANGELOG updated: fixed duplicate click counting under concurrent load; no public API changes. " +
                "Added a note to architecture.md about the request-coalescing pattern for future cache work."),
            [StageId.ReleaseReadiness] = new ReleaseReadinessAgent(changeTicket: "JIRA-4588"),
        };

        var approvals = new ScriptedApprovalProvider(new Dictionary<StageId, ApprovalResponse>
        {
            [StageId.Requirements] = new(ApprovalDecision.Approved, "eng-lead", "Bug confirmed in production analytics; scope is a targeted fix, approved."),
            [StageId.Architecture] = new(ApprovalDecision.Approved, "eng-lead", "Request-coalescing is the right level of complexity for a single-instance deployment."),
            [StageId.ReleaseReadiness] = new(ApprovalDecision.Approved, "release-manager", "Regression reproduced then verified fixed under load; approved to ship."),
        });

        var guardrails = new IPolicyGuardrail[] { new SecretScanningGuardrail(), new DataPrivacyGuardrail(), new ChangeControlGuardrail() };

        var options = new OrchestrationOptions
        {
            RollbackTargets = new Dictionary<StageId, StageId>
            {
                [StageId.IntegrationTesting] = StageId.Implementation,
                [StageId.UnitTesting] = StageId.Implementation,
            },
            MaxRollbacks = 1
        };

        var engine = new OrchestrationEngine(graph, agents, approvals, guardrails, options, correlationId: "brownfield-run");
        var context = new PipelineExecutionContext("brownfield-run");

        return (engine, context, description);
    }
}
