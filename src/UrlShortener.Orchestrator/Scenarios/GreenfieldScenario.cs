using UrlShortener.Orchestrator.Agents;
using UrlShortener.Orchestrator.Engine;
using UrlShortener.Orchestrator.Governance;
using UrlShortener.Orchestrator.Graph;

namespace UrlShortener.Orchestrator.Scenarios;

/// <summary>
/// Greenfield: a new feature built from scratch on a clean, well-defined
/// requirement. Demonstrates the baseline happy path plus a bounded,
/// self-healing retry (flaky test infra) that recovers without rollback.
/// </summary>
public static class GreenfieldScenario
{
    public static (OrchestrationEngine Engine, PipelineExecutionContext Context, string Description) Build()
    {
        const string description =
            "Add QR code generation: GET /api/v1/urls/{code}/qrcode returns a PNG QR code " +
            "encoding the short link, so print/offline channels can use shortened URLs.";

        var graph = GraphFactory.BuildStandardSdlcGraph(implementationMaxRetries: 1, testingMaxRetries: 1);

        var unitTestAttempt = 0;
        var agents = new Dictionary<StageId, IAgent>
        {
            [StageId.Requirements] = new RequirementsAgent(
                "Add an endpoint that returns a QR code image (PNG) encoding the short URL for a given code."),

            [StageId.Architecture] = new ArchitectureAgent(
                designSummary: "Add GET /api/v1/urls/{code}/qrcode; new QrCodeService (QRCoder) generates PNG on demand; " +
                               "reuse ICacheService to cache rendered images by code; no schema changes required.",
                impactedModules: new[] { "UrlShortener.Api/Endpoints", "UrlShortener.Infrastructure/Services (new QrCodeService)" },
                tradeOffs: new[]
                {
                    "On-demand PNG generation chosen over precomputed storage to avoid unbounded blob growth.",
                    "In-memory cache is sufficient for prototype QPS; documented as a scale limitation."
                }),

            [StageId.Implementation] = new ImplementationAgent(
                filesOnSuccess: new[]
                {
                    "src/UrlShortener.Infrastructure/Services/QrCodeService.cs",
                    "src/UrlShortener.Api/Endpoints/QrCodeEndpoints.cs"
                }),

            [StageId.UnitTesting] = new UnitTestingAgent(
                testCount: 18,
                shouldFail: attempt => Interlocked.Increment(ref unitTestAttempt) == 1,
                failureReason: "1 unit test failed: image-diff assertion flaked on the CI runner (known-flaky infra, not a code defect)."),

            [StageId.IntegrationTesting] = new IntegrationTestingAgent(
                scenariosOnSuccess: new[]
                {
                    "GET /qrcode returns 200 image/png for an active code",
                    "GET /qrcode returns 404 for an unknown code",
                    "GET /qrcode returns 410 for an expired code"
                }),

            [StageId.SecurityReview] = new SecurityReviewAgent(),
            [StageId.Documentation] = new DocumentationAgent(
                "README + OpenAPI updated: new GET /api/v1/urls/{code}/qrcode endpoint, response content-type image/png, " +
                "404/410 error semantics documented to match the existing redirect endpoint."),
            [StageId.ReleaseReadiness] = new ReleaseReadinessAgent(changeTicket: "JIRA-4521"),
        };

        var approvals = new ScriptedApprovalProvider(new Dictionary<StageId, ApprovalResponse>
        {
            [StageId.Requirements] = new(ApprovalDecision.Approved, "eng-lead", "Clear, well-scoped requirement."),
            [StageId.Architecture] = new(ApprovalDecision.Approved, "eng-lead", "Design is minimal and reuses existing caching."),
            [StageId.ReleaseReadiness] = new(ApprovalDecision.Approved, "release-manager", "All gates green; ship it."),
        });

        var guardrails = new IPolicyGuardrail[] { new SecretScanningGuardrail(), new DataPrivacyGuardrail(), new ChangeControlGuardrail() };

        var options = new OrchestrationOptions
        {
            RollbackTargets = new Dictionary<StageId, StageId> { [StageId.IntegrationTesting] = StageId.Implementation },
            MaxRollbacks = 1
        };

        var engine = new OrchestrationEngine(graph, agents, approvals, guardrails, options, correlationId: "greenfield-run");
        var context = new PipelineExecutionContext("greenfield-run");

        return (engine, context, description);
    }
}
