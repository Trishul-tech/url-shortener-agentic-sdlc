using UrlShortener.Orchestrator.Graph;

namespace UrlShortener.Orchestrator.Governance;

public sealed record ApprovalRequest(StageId Stage, string Summary, string RiskLevel, IReadOnlyDictionary<string, string> Context);

public enum ApprovalDecision { Approved, Rejected, Deferred }

public sealed record ApprovalResponse(
    ApprovalDecision Decision,
    string RespondedBy,
    string? Rationale,
    IReadOnlyDictionary<string, string>? Clarifications = null);

/// <summary>
/// Human approval checkpoint. Kept as an interface so the same engine runs
/// unattended in CI with a scripted provider and interactively with a
/// console provider - "controlled autonomy" means the checkpoint is always
/// in the graph, only its implementation changes.
/// </summary>
public interface IApprovalProvider
{
    Task<ApprovalResponse> RequestApprovalAsync(ApprovalRequest request, CancellationToken ct);
}

/// <summary>
/// Approvals driven by a pre-scripted decision table, keyed by stage. Used
/// by the reproducible scenario demos in this repo. Falls back to
/// auto-approve for any stage not explicitly scripted, so tests don't need
/// to enumerate every checkpoint.
/// </summary>
public sealed class ScriptedApprovalProvider : IApprovalProvider
{
    private readonly IReadOnlyDictionary<StageId, ApprovalResponse> _script;

    public ScriptedApprovalProvider(IReadOnlyDictionary<StageId, ApprovalResponse> script) => _script = script;

    public Task<ApprovalResponse> RequestApprovalAsync(ApprovalRequest request, CancellationToken ct)
    {
        var response = _script.TryGetValue(request.Stage, out var scripted)
            ? scripted
            : new ApprovalResponse(ApprovalDecision.Approved, "auto-approver", "No script entry; default-approved for demo purposes.");

        return Task.FromResult(response);
    }
}

/// <summary>Interactive console approval - a real human types y/n. Used when running the orchestrator with --interactive.</summary>
public sealed class ConsoleApprovalProvider : IApprovalProvider
{
    public Task<ApprovalResponse> RequestApprovalAsync(ApprovalRequest request, CancellationToken ct)
    {
        Console.WriteLine();
        Console.WriteLine($"[APPROVAL REQUIRED] Stage={request.Stage} Risk={request.RiskLevel}");
        Console.WriteLine($"  {request.Summary}");
        foreach (var kv in request.Context)
            Console.WriteLine($"    {kv.Key}: {kv.Value}");
        Console.Write("  Approve? [y/N]: ");

        var input = Console.ReadLine();
        var approved = string.Equals(input?.Trim(), "y", StringComparison.OrdinalIgnoreCase);

        return Task.FromResult(new ApprovalResponse(
            approved ? ApprovalDecision.Approved : ApprovalDecision.Rejected,
            "interactive-user",
            approved ? "Approved via console." : "Rejected via console."));
    }
}
