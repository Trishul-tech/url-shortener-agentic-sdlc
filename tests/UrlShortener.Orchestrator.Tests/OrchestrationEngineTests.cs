using FluentAssertions;
using UrlShortener.Orchestrator.Agents;
using UrlShortener.Orchestrator.Engine;
using UrlShortener.Orchestrator.Governance;
using UrlShortener.Orchestrator.Graph;
using Xunit;

namespace UrlShortener.Orchestrator.Tests;

public class OrchestrationEngineTests
{
    private static readonly OrchestrationOptions FastOptions = new() { SimulatedWorkDelay = TimeSpan.Zero };

    private static ScriptedApprovalProvider AutoApprove() => new(new Dictionary<StageId, ApprovalResponse>());

    [Fact]
    public async Task RunAsync_WithAllStagesSucceeding_CompletesPipeline()
    {
        var graph = new DependencyGraph(new[]
        {
            StageNode.Root(StageId.Requirements, "Requirements"),
            new StageNode(StageId.Architecture, "Architecture", new[] { StageId.Requirements }),
        });

        var agents = new Dictionary<StageId, IAgent>
        {
            [StageId.Requirements] = TestAgent.AlwaysSucceeds(StageId.Requirements),
            [StageId.Architecture] = TestAgent.AlwaysSucceeds(StageId.Architecture),
        };

        var engine = new OrchestrationEngine(graph, agents, AutoApprove(), Array.Empty<IPolicyGuardrail>(), FastOptions);
        var result = await engine.RunAsync(new PipelineExecutionContext("test"));

        result.Status.Should().Be(PipelineStatus.Completed);
        result.FinalStageStates.Values.Should().OnlyContain(s => s.Status == StageStatus.Completed);
        result.Metrics.SuccessRate.Should().Be(1.0);
    }

    [Fact]
    public async Task RunAsync_WithTransientFailure_RetriesWithinBudgetAndSucceeds()
    {
        var graph = new DependencyGraph(new[] { new StageNode(StageId.Implementation, "Impl", Array.Empty<StageId>(), MaxRetries: 2) });
        var agent = TestAgent.FailsThenSucceeds(StageId.Implementation, failuresBeforeSuccess: 2);
        var agents = new Dictionary<StageId, IAgent> { [StageId.Implementation] = agent };

        var engine = new OrchestrationEngine(graph, agents, AutoApprove(), Array.Empty<IPolicyGuardrail>(), FastOptions);
        var result = await engine.RunAsync(new PipelineExecutionContext("test"));

        result.Status.Should().Be(PipelineStatus.Completed);
        agent.CallCount.Should().Be(3);
        result.Metrics.RetryCount.Should().Be(2);
    }

    [Fact]
    public async Task RunAsync_WhenRetriesExhaustedWithNoRollbackTarget_SafeStops()
    {
        var graph = new DependencyGraph(new[] { new StageNode(StageId.Implementation, "Impl", Array.Empty<StageId>(), MaxRetries: 1) });
        var agents = new Dictionary<StageId, IAgent> { [StageId.Implementation] = TestAgent.AlwaysFails(StageId.Implementation) };

        var engine = new OrchestrationEngine(graph, agents, AutoApprove(), Array.Empty<IPolicyGuardrail>(), FastOptions);
        var result = await engine.RunAsync(new PipelineExecutionContext("test"));

        result.Status.Should().Be(PipelineStatus.SafeStopped);
        result.SafeStopReason.Should().Contain("retry budget exhausted");
    }

    [Fact]
    public async Task RunAsync_WhenDownstreamStageExhaustsRetries_RollsBackAndReplansUpstreamThenRecovers()
    {
        var graph = new DependencyGraph(new[]
        {
            StageNode.Root(StageId.Implementation, "Implementation"),
            new StageNode(StageId.IntegrationTesting, "Integration", new[] { StageId.Implementation }, MaxRetries: 1),
        });

        var implRuns = 0;
        var implAgent = new TestAgent(StageId.Implementation, _ => { implRuns++; return AgentOutcome.Ok("implemented"); });
        // Fails while Implementation has only run once; succeeds after the rework re-execution.
        var testAgent = new TestAgent(StageId.IntegrationTesting, _ => implRuns <= 1 ? AgentOutcome.Fail("regression found") : AgentOutcome.Ok("fixed"));

        var agents = new Dictionary<StageId, IAgent> { [StageId.Implementation] = implAgent, [StageId.IntegrationTesting] = testAgent };
        var options = new OrchestrationOptions
        {
            SimulatedWorkDelay = TimeSpan.Zero,
            RollbackTargets = new Dictionary<StageId, StageId> { [StageId.IntegrationTesting] = StageId.Implementation },
            MaxRollbacks = 1
        };

        var engine = new OrchestrationEngine(graph, agents, AutoApprove(), Array.Empty<IPolicyGuardrail>(), options);
        var result = await engine.RunAsync(new PipelineExecutionContext("test"));

        result.Status.Should().Be(PipelineStatus.Completed);
        result.Metrics.RollbackCount.Should().Be(1);
        result.Metrics.ReplanCount.Should().Be(1);
        implRuns.Should().Be(2);
    }

    [Fact]
    public async Task RunAsync_WhenRollbackBudgetExhausted_SafeStops()
    {
        var graph = new DependencyGraph(new[]
        {
            StageNode.Root(StageId.Implementation, "Implementation"),
            new StageNode(StageId.IntegrationTesting, "Integration", new[] { StageId.Implementation }, MaxRetries: 0),
        });

        var agents = new Dictionary<StageId, IAgent>
        {
            [StageId.Implementation] = TestAgent.AlwaysSucceeds(StageId.Implementation),
            [StageId.IntegrationTesting] = TestAgent.AlwaysFails(StageId.IntegrationTesting),
        };
        var options = new OrchestrationOptions
        {
            SimulatedWorkDelay = TimeSpan.Zero,
            RollbackTargets = new Dictionary<StageId, StageId> { [StageId.IntegrationTesting] = StageId.Implementation },
            MaxRollbacks = 1
        };

        var engine = new OrchestrationEngine(graph, agents, AutoApprove(), Array.Empty<IPolicyGuardrail>(), options);
        var result = await engine.RunAsync(new PipelineExecutionContext("test"));

        result.Status.Should().Be(PipelineStatus.SafeStopped);
        result.SafeStopReason.Should().Contain("Rollback budget exhausted");
    }

    [Fact]
    public async Task RunAsync_WithBlockingGuardrail_SafeStopsImmediately()
    {
        var graph = new DependencyGraph(new[] { StageNode.Root(StageId.Implementation, "Implementation") });
        var agents = new Dictionary<StageId, IAgent> { [StageId.Implementation] = TestAgent.AlwaysSucceeds(StageId.Implementation) };
        var guardrails = new IPolicyGuardrail[] { new AlwaysBlockGuardrail() };

        var engine = new OrchestrationEngine(graph, agents, AutoApprove(), guardrails, FastOptions);
        var result = await engine.RunAsync(new PipelineExecutionContext("test"));

        result.Status.Should().Be(PipelineStatus.SafeStopped);
        result.SafeStopReason.Should().Contain("test-guardrail");
        result.FinalStageStates[StageId.Implementation].Status.Should().Be(StageStatus.FailedTerminal);
    }

    [Fact]
    public async Task RunAsync_WithRejectedApproval_SafeStops()
    {
        var graph = new DependencyGraph(new[]
        {
            new StageNode(StageId.Architecture, "Architecture", Array.Empty<StageId>(), RequiresHumanApproval: true),
        });
        var agents = new Dictionary<StageId, IAgent> { [StageId.Architecture] = TestAgent.AlwaysSucceeds(StageId.Architecture) };
        var approvals = new ScriptedApprovalProvider(new Dictionary<StageId, ApprovalResponse>
        {
            [StageId.Architecture] = new(ApprovalDecision.Rejected, "reviewer", "Design needs rework."),
        });

        var engine = new OrchestrationEngine(graph, agents, approvals, Array.Empty<IPolicyGuardrail>(), FastOptions);
        var result = await engine.RunAsync(new PipelineExecutionContext("test"));

        result.Status.Should().Be(PipelineStatus.SafeStopped);
        result.SafeStopReason.Should().Contain("rejected");
    }

    private sealed class AlwaysBlockGuardrail : IPolicyGuardrail
    {
        public string Name => "test-guardrail";
        public GuardrailFinding? Evaluate(StageId stage, IReadOnlyDictionary<string, object> context) =>
            new(Name, GuardrailSeverity.Blocking, "blocked for test");
    }
}
