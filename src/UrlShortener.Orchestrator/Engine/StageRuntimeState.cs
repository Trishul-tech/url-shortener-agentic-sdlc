namespace UrlShortener.Orchestrator.Engine;

public enum StageStatus { Pending, Running, AwaitingApproval, Completed, FailedTerminal }

public sealed class StageRuntimeState
{
    public StageStatus Status { get; set; } = StageStatus.Pending;
    public int Attempts { get; set; }
    public string? LastFailureReason { get; set; }

    /// <summary>Whether a fallback strategy has already been tried for this stage. Fallback is attempted at most once per stage failure, so it can't loop.</summary>
    public bool FallbackUsed { get; set; }
}