using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace UrlShortener.Api.IntegrationTests;

/// <summary>
/// End-to-end tests against a real in-memory TestServer for the orchestrator
/// HTTP surface: GET /runs (sample-run artifacts), GET/POST /runs/{scenario}
/// (start a live run), GET .../live/{runId} (poll), and POST .../approve|reject
/// (resolve a real pending approval). These exercise the exact code path
/// verified live during development - GreenfieldScenario/AmbiguousScenario.Build()
/// through the real OrchestrationEngine with HttpApprovalProvider.
/// </summary>
public class OrchestratorEndpointsTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public OrchestratorEndpointsTests(CustomWebApplicationFactory factory) =>
        _client = factory.CreateClient();

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>())!;

    [Fact]
    public async Task GetRuns_ReturnsKnownScenarioNames()
    {
        var response = await _client.GetAsync("/api/v1/orchestrator/runs");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var scenarios = await response.Content.ReadFromJsonAsync<string[]>();
        scenarios.Should().Contain(new[] { "greenfield", "brownfield", "ambiguous" });
    }

    [Fact]
    public async Task GetRunDetail_KnownScenario_ReturnsSampleRunArtifacts()
    {
        var response = await _client.GetAsync("/api/v1/orchestrator/runs/greenfield");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await ReadJsonAsync(response);
        body.GetProperty("scenario").GetString().Should().Be("greenfield");
        body.GetProperty("source").GetString().Should().Be("sample-run");
        body.GetProperty("artifacts").GetProperty("metrics").ValueKind.Should().NotBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task GetRunDetail_UnknownScenario_ReturnsNotFound()
    {
        var response = await _client.GetAsync("/api/v1/orchestrator/runs/not-a-real-scenario");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PostRun_UnknownScenario_ReturnsNotFound()
    {
        var response = await _client.PostAsync("/api/v1/orchestrator/runs/not-a-real-scenario", content: null);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PostRun_KnownScenario_ReturnsAcceptedWithRunId()
    {
        var response = await _client.PostAsync("/api/v1/orchestrator/runs/greenfield", content: null);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var body = await ReadJsonAsync(response);
        body.GetProperty("scenario").GetString().Should().Be("greenfield");
        body.GetProperty("status").GetString().Should().Be("Accepted");
        body.GetProperty("runId").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task LivePoll_UnknownRunId_ReturnsNotFound()
    {
        var response = await _client.GetAsync("/api/v1/orchestrator/runs/greenfield/live/does-not-exist");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Approve_UnknownRunId_ReturnsNotFound()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/v1/orchestrator/runs/greenfield/live/does-not-exist/approve",
            new { respondedBy = "test", rationale = "n/a" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PostRun_ThenPoll_EventuallyBlocksOnRequirementsApproval()
    {
        var start = await _client.PostAsync("/api/v1/orchestrator/runs/greenfield", content: null);
        var runId = (await ReadJsonAsync(start)).GetProperty("runId").GetString();

        var pending = await PollUntilAsync(
            "greenfield", runId!,
            body => HasPendingApproval(body));

        pending.GetProperty("pendingApproval").GetProperty("stage").GetString().Should().Be("Requirements");
        pending.GetProperty("status").GetString().Should().Be("Running");
    }

    [Fact]
    public async Task FullGreenfieldRun_ApprovedThroughEveryGate_Completes()
    {
        var start = await _client.PostAsync("/api/v1/orchestrator/runs/greenfield", content: null);
        var runId = (await ReadJsonAsync(start)).GetProperty("runId").GetString()!;

        // Requirements, Architecture and ReleaseReadiness are all approval-gated;
        // keep approving whatever gate is currently pending until the run finishes.
        for (var i = 0; i < 5; i++)
        {
            var current = await PollUntilAsync(
                "greenfield", runId,
                body => body.GetProperty("status").GetString() != "Running"
                        || HasPendingApproval(body));

            if (current.GetProperty("status").GetString() == "Completed")
            {
                current.GetProperty("finalPipelineStatus").GetString().Should().Be("Completed");
                return;
            }

            var approve = await _client.PostAsJsonAsync(
                $"/api/v1/orchestrator/runs/greenfield/live/{runId}/approve",
                new { respondedBy = "integration-test", rationale = "Automated approval in test." });
            approve.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        Assert.Fail("Run did not complete after 5 approvals.");
    }

    [Fact]
    public async Task AmbiguousRun_RejectedAtFirstGate_SafeStops()
    {
        var start = await _client.PostAsync("/api/v1/orchestrator/runs/ambiguous", content: null);
        var runId = (await ReadJsonAsync(start)).GetProperty("runId").GetString()!;

        await PollUntilAsync("ambiguous", runId, body => HasPendingApproval(body));

        var reject = await _client.PostAsJsonAsync(
            $"/api/v1/orchestrator/runs/ambiguous/live/{runId}/reject",
            new { respondedBy = "integration-test", rationale = "Rejecting in test." });
        reject.StatusCode.Should().Be(HttpStatusCode.OK);

        var final = await PollUntilAsync(
            "ambiguous", runId,
            body => body.GetProperty("status").GetString() == "Completed");

        final.GetProperty("finalPipelineStatus").GetString().Should().Contain("SafeStopped");
    }

    private static bool HasPendingApproval(JsonElement body) =>
        body.TryGetProperty("pendingApproval", out var pending) && pending.ValueKind == JsonValueKind.Object;

    private async Task<JsonElement> PollUntilAsync(string scenario, string runId, Func<JsonElement, bool> condition)
    {
        for (var i = 0; i < 50; i++)
        {
            var response = await _client.GetAsync($"/api/v1/orchestrator/runs/{scenario}/live/{runId}");
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var body = await ReadJsonAsync(response);
            if (condition(body))
            {
                return body;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException($"Condition not met for run {runId} after polling.");
    }
}