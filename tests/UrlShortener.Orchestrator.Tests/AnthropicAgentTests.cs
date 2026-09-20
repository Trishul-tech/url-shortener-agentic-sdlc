using System.Net;
using System.Net.Http;
using UrlShortener.Orchestrator.Agents;
using UrlShortener.Orchestrator.Engine;
using UrlShortener.Orchestrator.Graph;
using Xunit;

namespace UrlShortener.Orchestrator.Tests;

public class AnthropicAgentTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _responseBody;

        public FakeHandler(HttpStatusCode statusCode, string responseBody)
        {
            _statusCode = statusCode;
            _responseBody = responseBody;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseBody)
            };
            return Task.FromResult(response);
        }
    }

    [Fact]
    public async Task ExecuteAsync_OnSuccess_ReturnsOkWithModelText()
    {
        var fakeJson = "{\"content\":[{\"type\":\"text\",\"text\":\"Requirements look clear: build a URL shortener.\"}]}";
        var httpClient = new HttpClient(new FakeHandler(HttpStatusCode.OK, fakeJson));
        var agent = new AnthropicAgent(httpClient, "fake-api-key", StageId.Requirements, "Summarize the ask.");
        var context = new PipelineExecutionContext("test");

        var outcome = await agent.ExecuteAsync(context, attempt: 1, CancellationToken.None);

        Assert.True(outcome.Success);
        Assert.Equal("Requirements look clear: build a URL shortener.", outcome.Summary);
    }

    [Fact]
    public async Task ExecuteAsync_OnHttpError_ReturnsFail()
    {
        var httpClient = new HttpClient(new FakeHandler(HttpStatusCode.Unauthorized, "{\"error\":\"invalid api key\"}"));
        var agent = new AnthropicAgent(httpClient, "bad-key", StageId.Requirements, "Summarize the ask.");
        var context = new PipelineExecutionContext("test");

        var outcome = await agent.ExecuteAsync(context, attempt: 1, CancellationToken.None);

        Assert.False(outcome.Success);
        Assert.Contains("401", outcome.FailureReason);
    }
}
