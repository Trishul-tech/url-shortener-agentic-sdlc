using FluentAssertions;
using UrlShortener.Orchestrator.Engine;
using UrlShortener.Orchestrator.Graph;
using UrlShortener.Orchestrator.Scenarios;
using Xunit;

namespace UrlShortener.Orchestrator.Tests;

/// <summary>
/// Validates the three scenarios required by the assignment end-to-end
/// against the real engine (no mocking) - these double as executable
/// documentation of what each scenario is expected to demonstrate.
/// </summary>
public class ScenarioTests
{
    [Fact]
    public async Task Greenfield_CompletesWithOneSelfHealingRetryAndNoRollback()
    {
        var (engine, context, _) = GreenfieldScenario.Build();
        var result = await engine.RunAsync(context);

        result.Status.Should().Be(PipelineStatus.Completed);
        result.Metrics.RetryCount.Should().BeGreaterThan(0, "the unit-testing stage is scripted to flake once");
        result.Metrics.RollbackCount.Should().Be(0);
    }

    [Fact]
    public async Task Brownfield_RecoversViaRollbackAndReplanAfterRegression()
    {
        var (engine, context, _) = BrownfieldScenario.Build();
        var result = await engine.RunAsync(context);

        result.Status.Should().Be(PipelineStatus.Completed);
        result.Metrics.RollbackCount.Should().Be(1);
        result.Metrics.ReplanCount.Should().Be(1);
    }

    [Fact]
    public async Task Ambiguous_ClarifiesRequirementThenSafeStopsOnPolicyGuardrail()
    {
        var (engine, context, _) = AmbiguousScenario.Build();
        var result = await engine.RunAsync(context);

        result.Status.Should().Be(PipelineStatus.SafeStopped);
        result.SafeStopReason.Should().Contain("data-privacy");
        context.GetArtifact<string>("human_clarification").Should().NotBeNullOrEmpty();
        result.FinalStageStates[StageId.Requirements].Status.Should().Be(StageStatus.Completed);
    }
}
