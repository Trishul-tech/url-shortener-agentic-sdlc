using UrlShortener.Orchestrator.Agents;
using UrlShortener.Orchestrator.Engine;
using UrlShortener.Orchestrator.Graph;

namespace UrlShortener.Orchestrator.Tests;

/// <summary>Minimal scriptable agent for engine-level tests - decouples engine tests from the real SDLC agents.</summary>
public sealed class TestAgent : IAgent
{
    public StageId Stage { get; }
    public int CallCount { get; private set; }

    private readonly Func<int, AgentOutcome> _behavior;

    public TestAgent(StageId stage, Func<int, AgentOutcome>? behavior = null)
    {
        Stage = stage;
        _behavior = behavior ?? (_ => AgentOutcome.Ok("ok"));
    }

    public static TestAgent AlwaysSucceeds(StageId stage) => new(stage);

    public static TestAgent FailsThenSucceeds(StageId stage, int failuresBeforeSuccess) =>
        new(stage, attempt => attempt <= failuresBeforeSuccess ? AgentOutcome.Fail("simulated transient failure") : AgentOutcome.Ok("recovered"));

    public static TestAgent AlwaysFails(StageId stage, string reason = "simulated permanent failure") =>
        new(stage, _ => AgentOutcome.Fail(reason));

    public Task<AgentOutcome> ExecuteAsync(PipelineExecutionContext context, int attempt, CancellationToken ct)
    {
        CallCount++;
        return Task.FromResult(_behavior(attempt));
    }
}
