using UrlShortener.Orchestrator.Agents;
using UrlShortener.Orchestrator.Engine;
using UrlShortener.Orchestrator.Governance;
using UrlShortener.Orchestrator.Graph;

namespace UrlShortener.Orchestrator.Scenarios;

/// <summary>
/// Ambiguous: a vague, underspecified ask. Demonstrates two governance
/// paths in one run: (1) the Requirements approval checkpoint surfacing
/// detected ambiguity and capturing a human clarification before any design
/// work starts, and (2) a policy guardrail catching a privacy-risk design
/// decision as soon as Implementation produces it and safe-stopping the
/// pipeline - a controlled halt that requires human rework, not an
/// automatic retry, because the problem is a policy violation, not a
/// transient fault.
/// </summary>
public static class AmbiguousScenario
{
    public static (OrchestrationEngine Engine, PipelineExecutionContext Context, string Description) Build()
    {
        const string description =
            "\"Make the analytics better.\" - an underspecified ask with no acceptance criteria, " +
            "requiring clarification before any design or implementation work can start.";

        var graph = GraphFactory.BuildStandardSdlcGraph(implementationMaxRetries: 1, testingMaxRetries: 1);

        var agents = new Dictionary<StageId, IAgent>
        {
            [StageId.Requirements] = new RequirementsAgent("Make the analytics better."),

            [StageId.Architecture] = new ArchitectureAgent(
                designSummary: "Add per-country click breakdown to the analytics endpoint. To maximize geolocation " +
                               "accuracy, persist the raw client IP address on each ClickEvent (in addition to the " +
                               "existing hash) so a batch job can resolve country server-side.",
                impactedModules: new[]
                {
                    "UrlShortener.Domain/Entities/ClickEvent.cs",
                    "UrlShortener.Application/Analytics/GetUrlAnalytics",
                    "UrlShortener.Infrastructure/Persistence/Repositories/ClickEventRepository.cs"
                },
                tradeOffs: new[]
                {
                    "Raw IP storage improves geolocation accuracy over IP-hash-only, at the cost of a PII retention question - flagged for the security/compliance gate."
                }),

            // Success at the code-generation level, but the artifact itself trips the data-privacy guardrail.
            [StageId.Implementation] = new ImplementationAgent(
                filesOnSuccess: new[]
                {
                    "src/UrlShortener.Domain/Entities/ClickEvent.cs",
                    "src/UrlShortener.Application/Analytics/GetUrlAnalytics/GetUrlAnalyticsHandler.cs"
                },
                exposesRawIp: true),

            [StageId.UnitTesting] = new UnitTestingAgent(testCount: 10),
            [StageId.IntegrationTesting] = new IntegrationTestingAgent(new[] { "Per-country breakdown returns expected counts" }),
            [StageId.SecurityReview] = new SecurityReviewAgent(),
            [StageId.Documentation] = new DocumentationAgent("Analytics endpoint docs updated with country breakdown field."),
            [StageId.ReleaseReadiness] = new ReleaseReadinessAgent(changeTicket: "JIRA-4610"),
        };

        var approvals = new ScriptedApprovalProvider(new Dictionary<StageId, ApprovalResponse>
        {
            [StageId.Requirements] = new(
                ApprovalDecision.Approved,
                "product-owner",
                "Clarified scope with the requester.",
                Clarifications: new Dictionary<string, string>
                {
                    ["human_clarification"] = "Add per-country breakdown of clicks to the analytics endpoint using IP geolocation. " +
                                               "Everything else ('better') is out of scope for this change."
                }),
        });

        var guardrails = new IPolicyGuardrail[] { new SecretScanningGuardrail(), new DataPrivacyGuardrail(), new ChangeControlGuardrail() };

        var options = new OrchestrationOptions
        {
            RollbackTargets = new Dictionary<StageId, StageId> { [StageId.IntegrationTesting] = StageId.Implementation },
            MaxRollbacks = 1
        };

        var engine = new OrchestrationEngine(graph, agents, approvals, guardrails, options, correlationId: "ambiguous-run");
        var context = new PipelineExecutionContext("ambiguous-run");

        return (engine, context, description);
    }
}
