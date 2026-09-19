using UrlShortener.Orchestrator.Engine;
using UrlShortener.Orchestrator.Graph;

namespace UrlShortener.Orchestrator.Agents;

/// <summary>Runs (simulated) unit tests against the Implementation stage's artifacts.</summary>
public sealed class UnitTestingAgent : IAgent
{
    public StageId Stage => StageId.UnitTesting;

    private readonly int _testCount;
    private readonly Func<int, bool> _shouldFail;
    private readonly string _failureReason;

    public UnitTestingAgent(int testCount, Func<int, bool>? shouldFail = null,
        string failureReason = "2 unit tests failed: caching edge case not covered.")
    {
        _testCount = testCount;
        _shouldFail = shouldFail ?? (_ => false);
        _failureReason = failureReason;
    }

    public Task<AgentOutcome> ExecuteAsync(PipelineExecutionContext context, int attempt, CancellationToken ct)
    {
        if (_shouldFail(attempt))
            return Task.FromResult(AgentOutcome.Fail(_failureReason));

        context.SetArtifact("unit_test_results", $"{_testCount}/{_testCount} passed");
        context.RecordDecision(Stage, "UnitTestsPassed", $"{_testCount}/{_testCount} passed", "UnitTestingAgent");
        return Task.FromResult(AgentOutcome.Ok($"{_testCount}/{_testCount} unit tests passed."));
    }
}

/// <summary>Runs (simulated) integration tests exercising the API surface end-to-end.</summary>
public sealed class IntegrationTestingAgent : IAgent
{
    public StageId Stage => StageId.IntegrationTesting;

    private readonly IReadOnlyList<string> _scenariosOnSuccess;
    private readonly Func<int, bool> _shouldFail;
    private readonly string _failureReason;

    public IntegrationTestingAgent(IReadOnlyList<string> scenariosOnSuccess, Func<int, bool>? shouldFail = null,
        string failureReason = "Integration regression: redirect returns stale target after deactivate+reactivate.")
    {
        _scenariosOnSuccess = scenariosOnSuccess;
        _shouldFail = shouldFail ?? (_ => false);
        _failureReason = failureReason;
    }

    public Task<AgentOutcome> ExecuteAsync(PipelineExecutionContext context, int attempt, CancellationToken ct)
    {
        if (_shouldFail(attempt))
        {
            context.RecordDecision(Stage, "IntegrationTestsFailed", _failureReason, "IntegrationTestingAgent");
            return Task.FromResult(AgentOutcome.Fail(_failureReason));
        }

        context.SetArtifact("integration_test_results", _scenariosOnSuccess);
        context.RecordDecision(Stage, "IntegrationTestsPassed", string.Join(", ", _scenariosOnSuccess), "IntegrationTestingAgent");
        return Task.FromResult(AgentOutcome.Ok($"{_scenariosOnSuccess.Count} integration scenario(s) passed."));
    }
}
