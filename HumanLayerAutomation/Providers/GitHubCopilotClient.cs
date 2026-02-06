using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace HumanLayerAutomation.Providers;

/// <summary>
/// HTTP client for the GitHub Copilot Chat Completions API.
/// Uses the GitHub Models endpoint (api.githubcopilot.com) available with GitHub Pro.
/// Handles token auth, retries, and streaming.
/// </summary>
public class GitHubCopilotClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger? _logger;
    private readonly GitHubCopilotClientOptions _options;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    public GitHubCopilotClient(GitHubCopilotClientOptions? options = null, ILogger? logger = null)
    {
        _options = options ?? new GitHubCopilotClientOptions();
        _logger = logger;

        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(_options.BaseUrl),
            Timeout = TimeSpan.FromMinutes(_options.TimeoutMinutes)
        };

        var token = _options.Token
            ?? Environment.GetEnvironmentVariable("GITHUB_TOKEN")
            ?? Environment.GetEnvironmentVariable("GH_TOKEN");

        if (!string.IsNullOrEmpty(token))
        {
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);
        }

        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "dotnet-automation/1.0");

        // GitHub API version header (required)
        if (!string.IsNullOrEmpty(_options.ApiVersion))
        {
            _httpClient.DefaultRequestHeaders.Add("X-GitHub-Api-Version", _options.ApiVersion);
        }
    }

    /// <summary>Check if the API is reachable and authenticated.</summary>
    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        var token = _httpClient.DefaultRequestHeaders.Authorization?.Parameter;
        if (string.IsNullOrEmpty(token))
        {
            _logger?.LogDebug("No GitHub token configured for Copilot API");
            return false;
        }

        try
        {
            // Minimal request to verify auth — use the catalog endpoint
            var request = new HttpRequestMessage(HttpMethod.Get, "catalog/models");
            var response = await _httpClient.SendAsync(request, ct);

            if (response.IsSuccessStatusCode)
            {
                return true;
            }

            // Some endpoints return 404 for models list but still work for chat
            // Fall back to a tiny chat completion to verify
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return await TryMinimalChatAsync(ct);
            }

            _logger?.LogDebug("Copilot API returned {Status}", response.StatusCode);
            return false;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Copilot API availability check failed");
            return false;
        }
    }

    /// <summary>Send a chat completion request and return the full response.</summary>
    public async Task<CopilotChatResponse> ChatAsync(
        CopilotChatRequest request,
        CancellationToken ct = default)
    {
        var jsonContent = JsonSerializer.Serialize(request, JsonOptions);
        var httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/json");

        _logger?.LogInformation("Copilot API: {Model}, {Messages} messages",
            request.Model, request.Messages.Count);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsync("inference/chat/completions", httpContent, ct);
        }
        catch (TaskCanceledException)
        {
            return new CopilotChatResponse
            {
                Error = new CopilotError { Message = "Request timed out", Type = "timeout" }
            };
        }

        var responseJson = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger?.LogWarning("Copilot API error: {Status} - {Body}",
                response.StatusCode, Truncate(responseJson, 200));

            var errorResponse = new CopilotChatResponse
            {
                Error = new CopilotError
                {
                    Message = $"HTTP {(int)response.StatusCode}: {responseJson}",
                    Type = response.StatusCode.ToString(),
                    StatusCode = (int)response.StatusCode
                }
            };

            // Parse structured error if available
            try
            {
                var parsed = JsonSerializer.Deserialize<CopilotChatResponse>(responseJson, JsonOptions);
                if (parsed?.Error != null)
                {
                    parsed.Error.StatusCode = (int)response.StatusCode;
                    return parsed;
                }
            }
            catch { /* Use generic error */ }

            return errorResponse;
        }

        try
        {
            var result = JsonSerializer.Deserialize<CopilotChatResponse>(responseJson, JsonOptions);
            return result ?? new CopilotChatResponse
            {
                Error = new CopilotError { Message = "Failed to deserialize response" }
            };
        }
        catch (JsonException ex)
        {
            _logger?.LogWarning(ex, "Failed to parse Copilot response");
            return new CopilotChatResponse
            {
                Error = new CopilotError { Message = $"JSON parse error: {ex.Message}" }
            };
        }
    }

    /// <summary>
    /// Send a chat completion with automatic retry on rate-limit (429) or server errors (5xx).
    /// </summary>
    public async Task<CopilotChatResponse> ChatWithRetryAsync(
        CopilotChatRequest request,
        int maxRetries = 3,
        CancellationToken ct = default)
    {
        CopilotChatResponse? lastResponse = null;

        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            if (attempt > 0)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt)); // exponential backoff
                _logger?.LogInformation("Retrying in {Delay}s (attempt {Attempt}/{Max})",
                    delay.TotalSeconds, attempt, maxRetries);
                await Task.Delay(delay, ct);
            }

            lastResponse = await ChatAsync(request, ct);

            if (lastResponse.Error == null)
                return lastResponse;

            var statusCode = lastResponse.Error.StatusCode;

            // Retry on rate limit or server errors
            if (statusCode == 429 || (statusCode >= 500 && statusCode < 600))
            {
                _logger?.LogWarning("Retryable error: {Error}", lastResponse.Error.Message);
                continue;
            }

            // Non-retryable error
            break;
        }

        return lastResponse!;
    }

    private async Task<bool> TryMinimalChatAsync(CancellationToken ct)
    {
        try
        {
            var request = new CopilotChatRequest
            {
                Model = "openai/gpt-4o",
                Messages = [new CopilotMessage { Role = "user", Content = "Say OK" }],
                MaxTokens = 5
            };

            var response = await ChatAsync(request, ct);
            return response.Error == null;
        }
        catch
        {
            return false;
        }
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..(max - 3)] + "...";

    public void Dispose()
    {
        _httpClient.Dispose();
        GC.SuppressFinalize(this);
    }
}

// ============================================================================
// Configuration
// ============================================================================

public class GitHubCopilotClientOptions
{
    /// <summary>Base URL for the GitHub Models API.</summary>
    public string BaseUrl { get; set; } = "https://models.github.ai/";

    /// <summary>GitHub token (PAT or OAuth). Falls back to GITHUB_TOKEN / GH_TOKEN env vars.
    /// Token must have 'models:read' scope for fine-grained PATs.</summary>
    public string? Token { get; set; }

    /// <summary>Request timeout in minutes.</summary>
    public int TimeoutMinutes { get; set; } = 5;

    /// <summary>GitHub API version header.</summary>
    public string ApiVersion { get; set; } = "2022-11-28";
}

// ============================================================================
// Request / Response Models
// ============================================================================

public class CopilotChatRequest
{
    public string Model { get; set; } = "openai/gpt-4o";
    public List<CopilotMessage> Messages { get; set; } = [];
    public int? MaxTokens { get; set; }
    public double Temperature { get; set; } = 0.1;

    [JsonPropertyName("top_p")]
    public double? TopP { get; set; }

    [JsonPropertyName("stream")]
    public bool Stream { get; set; } = false;
}

public class CopilotMessage
{
    public string Role { get; set; } = "";
    public string Content { get; set; } = "";
}

public class CopilotChatResponse
{
    public string? Id { get; set; }
    public string? Object { get; set; }
    public long? Created { get; set; }
    public string? Model { get; set; }
    public List<CopilotChoice>? Choices { get; set; }
    public CopilotUsage? Usage { get; set; }
    public CopilotError? Error { get; set; }
}

public class CopilotChoice
{
    public int Index { get; set; }
    public CopilotMessage? Message { get; set; }

    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; set; }
}

public class CopilotUsage
{
    [JsonPropertyName("prompt_tokens")]
    public int PromptTokens { get; set; }

    [JsonPropertyName("completion_tokens")]
    public int CompletionTokens { get; set; }

    [JsonPropertyName("total_tokens")]
    public int TotalTokens { get; set; }
}

public class CopilotError
{
    public string Message { get; set; } = "";
    public string? Type { get; set; }
    public string? Code { get; set; }

    [JsonIgnore]
    public int StatusCode { get; set; }
}
