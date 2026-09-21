using UrlShortener.Api.Orchestration;
using UrlShortener.Orchestrator.Graph;
using UrlShortener.Orchestrator.Governance;
using Xunit;

namespace UrlShortener.Api.IntegrationTests;

public class HttpApprovalProviderTests
{
    private static ApprovalRequest SampleRequest() =>
        new(StageId.Requirements, "Sign off on requirement.", "Low", new Dictionary<string, string>());

    [Fact]
    public async Task RequestApprovalAsync_ResolvedByTryResolve_ReturnsMatchingDecision()
    {
        var provider = new HttpApprovalProvider();
        var requestTask = provider.RequestApprovalAsync(SampleRequest(), CancellationToken.None);

        // Give RequestApprovalAsync a moment to register the pending approval.
        await Task.Delay(20);
        var resolved = provider.TryResolve(ApprovalDecision.Approved, "trishul", "Looks good.");

        Assert.True(resolved);
        var response = await requestTask;
        Assert.Equal(ApprovalDecision.Approved, response.Decision);
        Assert.Equal("trishul", response.RespondedBy);
        Assert.Equal("Looks good.", response.Rationale);
    }

    [Fact]
    public async Task RequestApprovalAsync_RejectedByTryResolve_ReturnsRejected()
    {
        var provider = new HttpApprovalProvider();
        var requestTask = provider.RequestApprovalAsync(SampleRequest(), CancellationToken.None);

        await Task.Delay(20);
        provider.TryResolve(ApprovalDecision.Rejected, "trishul", "Needs more work.");

        var response = await requestTask;
        Assert.Equal(ApprovalDecision.Rejected, response.Decision);
    }

    [Fact]
    public void TryResolve_NoPendingRequest_ReturnsFalse()
    {
        var provider = new HttpApprovalProvider();
        var resolved = provider.TryResolve(ApprovalDecision.Approved, "trishul", "n/a");
        Assert.False(resolved);
    }

    [Fact]
    public async Task RequestApprovalAsync_ExposesPendingApprovalWhileWaiting()
    {
        var provider = new HttpApprovalProvider();
        var requestTask = provider.RequestApprovalAsync(SampleRequest(), CancellationToken.None);

        await Task.Delay(20);
        Assert.NotNull(provider.CurrentPending);
        Assert.Equal("Requirements", provider.CurrentPending!.Stage);

        provider.TryResolve(ApprovalDecision.Approved, "trishul", "ok");
        await requestTask;
        Assert.Null(provider.CurrentPending);
    }

    [Fact]
    public async Task RequestApprovalAsync_TimesOut_AutoRejects()
    {
        var provider = new HttpApprovalProvider(TimeSpan.FromMilliseconds(50));
        var response = await provider.RequestApprovalAsync(SampleRequest(), CancellationToken.None);

        Assert.Equal(ApprovalDecision.Rejected, response.Decision);
        Assert.Equal("timeout", response.RespondedBy);
    }

    [Fact]
    public async Task RequestApprovalAsync_ResolvedWithClarifications_CarriesThemThrough()
    {
        var provider = new HttpApprovalProvider();
        var requestTask = provider.RequestApprovalAsync(SampleRequest(), CancellationToken.None);

        await Task.Delay(20);
        var clarifications = new Dictionary<string, string> { ["requirements:ctx:feedback"] = "please clarify edge cases" };
        provider.TryResolve(ApprovalDecision.Deferred, "trishul", "needs more detail", clarifications);

        var response = await requestTask;
        Assert.Equal(ApprovalDecision.Deferred, response.Decision);
        Assert.NotNull(response.Clarifications);
        Assert.Equal("please clarify edge cases", response.Clarifications!["requirements:ctx:feedback"]);
    }
}
