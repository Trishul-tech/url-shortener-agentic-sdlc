namespace UrlShortener.Orchestrator.Engine;

public enum StageStatus { Pending, Running, AwaitingApproval, Completed, FailedTerminal }

public sealed class StageRuntimeState
{
    public StageStatus Status { get; set; } = StageStatus.Pending;
    public int Attempts { get; set; }
    public string? LastFailureReason { get; set; }
}
