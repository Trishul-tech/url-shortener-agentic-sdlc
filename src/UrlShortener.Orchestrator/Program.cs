using UrlShortener.Orchestrator.Scenarios;

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
                       $"rollbacks={r.Result.Metrics.RollbackCount} replans={r.Result.Metrics.ReplanCount} " +
                       $"successRate={r.Result.Metrics.SuccessRate:P0}");
}
