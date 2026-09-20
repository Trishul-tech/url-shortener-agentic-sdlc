using UrlShortener.Orchestrator.Scenarios;
using System.Net.Http;
using UrlShortener.Orchestrator.Agents;
using UrlShortener.Orchestrator.Engine;
using UrlShortener.Orchestrator.Graph;

var outputDir = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "artifacts", "sample-runs");
outputDir = Path.GetFullPath(outputDir);

Console.WriteLine("Agentic SDLC Orchestrator - URL Shortener");
Console.WriteLine($"Writing run artifacts to: {outputDir}");
Console.WriteLine(new string('=', 70));
Console.WriteLine();

var reports = new List<ScenarioReport>();

var (greenfieldEngine, greenfieldContext, greenfieldDesc) = GreenfieldScenario.Build();
reports.Add(await ScenarioRunner.RunAsync("Greenfield", greenfieldDesc, greenfieldEngine, greenfieldContext, outputDir));

var (brownfieldEngine, brownfieldContext, brownfieldDesc) = BrownfieldScenario.Build();
reports.Add(await ScenarioRunner.RunAsync("Brownfield", brownfieldDesc, brownfieldEngine, brownfieldContext, outputDir));

var (ambiguousEngine, ambiguousContext, ambiguousDesc) = AmbiguousScenario.Build();
reports.Add(await ScenarioRunner.RunAsync("Ambiguous", ambiguousDesc, ambiguousEngine, ambiguousContext, outputDir));

Console.WriteLine(new string('=', 70));
Console.WriteLine("Summary");
Console.WriteLine(new string('=', 70));
foreach (var r in reports)
{
    Console.WriteLine($"{r.Name,-12} status={r.Result.Status,-12} retries={r.Result.Metrics.RetryCount} " +
                       $"fallbacks={r.Result.Metrics.FallbackCount} rollbacks={r.Result.Metrics.RollbackCount} replans={r.Result.Metrics.ReplanCount} " +
                       $"successRate={r.Result.Metrics.SuccessRate:P0}");
}
// Optional live-LLM demonstration: if ANTHROPIC_API_KEY is set, run one
// real Anthropic API call through the exact same IAgent seam every
// simulated agent uses, proving the extensibility point documented in
// docs/architecture.md section 3.8 actually works end to end - not just
// described. The three required scenarios above are untouched and stay
// deterministic; this is purely additive.
var anthropicApiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
if (!string.IsNullOrWhiteSpace(anthropicApiKey))
{
    Console.WriteLine();
    Console.WriteLine(new string('=', 70));
    Console.WriteLine("Optional live-LLM demo (ANTHROPIC_API_KEY detected)");
    Console.WriteLine(new string('=', 70));

    using var httpClient = new HttpClient();
    var liveAgent = new AnthropicAgent(
        httpClient,
        anthropicApiKey,
        StageId.Requirements,
        "The ask is: \"Build a URL shortener with an agentic SDLC orchestrator.\" " +
        "In 2-3 sentences, restate this as a concrete engineering problem statement, " +
        "the same way the deterministic RequirementsAgent does.");

    var liveContext = new PipelineExecutionContext("live-llm-demo");
    var liveOutcome = await liveAgent.ExecuteAsync(liveContext, attempt: 1, CancellationToken.None);

    Console.WriteLine(liveOutcome.Success
        ? $"Live model response:\n{liveOutcome.Summary}"
        : $"Live call failed: {liveOutcome.FailureReason}");
}
else
{
    Console.WriteLine();
    Console.WriteLine("(Set ANTHROPIC_API_KEY to also run one real Anthropic API call " +
        "through the same IAgent seam - see docs/architecture.md section 3.8.)");
}
