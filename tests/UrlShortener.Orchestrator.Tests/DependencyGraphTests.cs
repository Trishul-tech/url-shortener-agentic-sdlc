using FluentAssertions;
using UrlShortener.Orchestrator.Graph;
using Xunit;

namespace UrlShortener.Orchestrator.Tests;

public class DependencyGraphTests
{
    [Fact]
    public void Constructor_WithCycle_Throws()
    {
        var nodes = new[]
        {
            new StageNode(StageId.Requirements, "A", new[] { StageId.Architecture }),
            new StageNode(StageId.Architecture, "B", new[] { StageId.Requirements }),
        };

        var act = () => new DependencyGraph(nodes);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ReadyStages_ReturnsOnlyStagesWithSatisfiedDependencies()
    {
        var graph = new DependencyGraph(new[]
        {
            StageNode.Root(StageId.Requirements, "Requirements"),
            new StageNode(StageId.Architecture, "Architecture", new[] { StageId.Requirements }),
            new StageNode(StageId.Implementation, "Implementation", new[] { StageId.Architecture }),
        });

        var ready = graph.ReadyStages(completed: new HashSet<StageId>(), inFlightOrDone: new HashSet<StageId>());
        ready.Should().ContainSingle().Which.Should().Be(StageId.Requirements);

        var readyAfterRequirements = graph.ReadyStages(
            completed: new HashSet<StageId> { StageId.Requirements },
            inFlightOrDone: new HashSet<StageId> { StageId.Requirements });
        readyAfterRequirements.Should().ContainSingle().Which.Should().Be(StageId.Architecture);
    }

    [Fact]
    public void ReadyStages_WithIndependentBranches_ReturnsBothInParallel()
    {
        var graph = new DependencyGraph(new[]
        {
            StageNode.Root(StageId.Implementation, "Implementation"),
            new StageNode(StageId.UnitTesting, "Unit", new[] { StageId.Implementation }),
            new StageNode(StageId.Documentation, "Docs", new[] { StageId.Implementation }),
        });

        var ready = graph.ReadyStages(
            completed: new HashSet<StageId> { StageId.Implementation },
            inFlightOrDone: new HashSet<StageId> { StageId.Implementation });

        ready.Should().BeEquivalentTo(new[] { StageId.UnitTesting, StageId.Documentation });
    }

    [Fact]
    public void TransitiveDependents_ReturnsFullDownstreamSet()
    {
        var graph = new DependencyGraph(new[]
        {
            StageNode.Root(StageId.Implementation, "Implementation"),
            new StageNode(StageId.UnitTesting, "Unit", new[] { StageId.Implementation }),
            new StageNode(StageId.ReleaseReadiness, "Release", new[] { StageId.UnitTesting }),
        });

        graph.TransitiveDependents(StageId.Implementation)
            .Should().BeEquivalentTo(new[] { StageId.UnitTesting, StageId.ReleaseReadiness });
    }
}
