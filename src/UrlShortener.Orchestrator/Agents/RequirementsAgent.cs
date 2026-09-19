using UrlShortener.Orchestrator.Engine;
using UrlShortener.Orchestrator.Graph;

namespace UrlShortener.Orchestrator.Agents;

/// <summary>
/// Interprets the raw ask, flags ambiguity, and normalizes it into a
/// concrete engineering problem statement. When ambiguities are found they
/// are surfaced through the stage's human-approval checkpoint (the node is
/// configured with RequiresHumanApproval) rather than guessed at silently.
/// </summary>
public sealed class RequirementsAgent : IAgent
{
    public StageId Stage => StageId.Requirements;

    private readonly string _rawRequirement;
    private readonly Func<string, IReadOnlyList<string>> _ambiguityDetector;

    public RequirementsAgent(string rawRequirement, Func<string, IReadOnlyList<string>>? ambiguityDetector = null)
    {
        _rawRequirement = rawRequirement;
        _ambiguityDetector = ambiguityDetector ?? DefaultDetector;
    }

    public Task<AgentOutcome> ExecuteAsync(PipelineExecutionContext context, int attempt, CancellationToken ct)
    {
        context.SetArtifact("raw_requirement", _rawRequirement);

        var ambiguities = _ambiguityDetector(_rawRequirement);
        context.SetArtifact("ambiguities", ambiguities);

        var normalized = ambiguities.Count == 0
            ? $"Normalized problem statement: {_rawRequirement.Trim()}"
            : $"Normalized problem statement (pending clarification): {_rawRequirement.Trim()}";

        context.SetArtifact("normalized_requirement", normalized);
        context.RecordDecision(Stage, "Normalized", normalized, "RequirementsAgent");

        var summaryLines = ambiguities.Count == 0
            ? "No ambiguity detected; requirement is well-defined."
            : $"{ambiguities.Count} ambiguity/ambiguities detected: {string.Join("; ", ambiguities)}";

        context.SetArtifact($"{Stage}:approval_summary",
            $"Sign off on normalized requirement. {summaryLines}");
        context.SetArtifact($"{Stage}:risk_level", ambiguities.Count == 0 ? "Low" : "Medium");
        foreach (var (a, i) in ambiguities.Select((a, i) => (a, i)))
            context.SetArtifact($"{Stage}:ctx:ambiguity_{i + 1}", a);

        return Task.FromResult(AgentOutcome.Ok(summaryLines));
    }

    private static IReadOnlyList<string> DefaultDetector(string requirement)
    {
        var vague = new[] { "better", "improve", "nice", "fast", "scalable", "modern", "simple" };
        var found = vague.Where(v => requirement.Contains(v, StringComparison.OrdinalIgnoreCase)).ToList();
        return found.Count == 0
            ? Array.Empty<string>()
            : found.Select(v => $"Term '{v}' is subjective and has no measurable acceptance criteria.").ToList();
    }
}
