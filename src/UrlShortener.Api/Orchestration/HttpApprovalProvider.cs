using UrlShortener.Orchestrator.Governance;

namespace UrlShortener.Api.Orchestration;

/// <summary>
/// IApprovalProvider that blocks a running stage on a TaskCompletionSource until
/// a human resolves it via POST .../live/{runId}/approve or /reject. This is the
/// real async-transport implementation the ApprovalDecision.Deferred value and
/// docs/architecture.md's "async transport (Slack/Teams/webhook)" note always
/// pointed at but never had a live caller for - a run started over HTTP is the
/// first. A 10-minute timeout auto-rejects an abandoned approval so a forgotten
/// run can't hang a background task forever.
/// </summary>
public sealed class HttpApprovalProvider : IApprovalProvider
{
    private readonly TimeSpan _timeout;
    private TaskCompletionSource<ApprovalResponse>? _pending;

    public HttpApprovalProvider(TimeSpan? timeout = null)
    {
        _timeout = timeout ?? TimeSpan.FromMinutes(10);
    }

    public PendingApprovalInfo? CurrentPending { get; private set; }

    public async Task<ApprovalResponse> RequestApprovalAsync(ApprovalRequest request, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<ApprovalResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending = tcs;
        CurrentPending = new PendingApprovalInfo(request.Stage.ToString(), request.Summary, request.RiskLevel);

        using var timeoutCts = new CancellationTokenSource(_timeout);
        using var reg = timeoutCts.Token.Register(() =>
            tcs.TrySetResult(new ApprovalResponse(ApprovalDecision.Rejected, "timeout", "No decision received within 10 minutes; auto-rejected.")));

        try
        {
            return await tcs.Task;
        }
        finally
        {
            _pending = null;
            CurrentPending = null;
        }
    }

    public bool TryResolve(ApprovalDecision decision, string respondedBy, string rationale)
    {
        return _pending?.TrySetResult(new ApprovalResponse(decision, respondedBy, rationale)) ?? false;
    }
}

public sealed record PendingApprovalInfo(string Stage, string Summary, string RiskLevel);