using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using UrlShortener.Orchestrator.Engine;
using UrlShortener.Orchestrator.Graph;

namespace UrlShortener.Orchestrator.Agents;

/// <summary>
/// Optional, opt-in IAgent implementation that calls the real Anthropic
/// Messages API instead of returning deterministic simulated output. This
/// is the concrete "swap the implementation, keep the engine unchanged"
/// demonstration promised in docs/architecture.md section 3.8 - it plugs
/// into the exact same IAgent seam every simulated agent (RequirementsAgent,
/// etc.) uses, with zero changes to the engine, gates, or audit log.
///
/// Not used by any of the three required scenarios by default - those stay
/// scripted and deterministic for reproducible review (see
/// docs/architecture.md section 3.8 for why). This agent only runs when a
/// caller explicitly constructs it with a real API key (see Program.cs,
/// which makes one live demo call only if ANTHROPIC_API_KEY is set). No
/// test in this repo calls the real network - AnthropicAgentTests injects
/// a fake HttpMessageHandler so the suite stays hermetic.
/// </summary>
public sealed class AnthropicAgent : IAgent
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly string _taskPrompt;

    public StageId Stage { get; }

    public AnthropicAgent(
        HttpClient httpClient,
        string apiKey,
        StageId stage,
        string taskPrompt,
        string model = "claude-sonnet-5")
    {
        _httpClient = httpClient;
        _apiKey = apiKey;
        Stage = stage;
        _taskPrompt = taskPrompt;
        _model = model;
    }

    public async Task<AgentOutcome> ExecuteAsync(PipelineExecutionContext context, int attempt, CancellationToken ct)
    {
        var requestBody = new
        {
            model = _model,
            max_tokens = 512,
            system = $"You are the {Stage} stage of a software SDLC orchestration pipeline for a URL shortener service. Respond with a concise, concrete artifact for this stage in plain text - a few sentences, no markdown code fences.",
            messages = new[]
            {
                new { role = "user", content = _taskPrompt }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
        request.Headers.Add("x-api-key", _apiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

        string body;
        HttpStatusCode statusCode;

        try
        {
            using var response = await _httpClient.SendAsync(request, ct);
            statusCode = response.StatusCode;
            body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                return AgentOutcome.Fail($"Anthropic API call failed ({(int)statusCode}): {body}");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return AgentOutcome.Fail($"Anthropic API call threw: {ex.Message}");
        }

        string text;
        try
        {
            using var doc = JsonDocument.Parse(body);
            text = doc.RootElement.GetProperty("content")[0].GetProperty("text").GetString() ?? string.Empty;
        }
        catch (Exception ex)
        {
            return AgentOutcome.Fail($"Could not parse Anthropic response: {ex.Message}");
        }

        context.SetArtifact($"{Stage}:live_llm_output", text);
        context.RecordDecision(Stage, "LiveLlmResponse", text, "AnthropicAgent");

        return AgentOutcome.Ok(text);
    }
}
