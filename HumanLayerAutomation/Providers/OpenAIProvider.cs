using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HumanLayerAutomation.Models;
using Microsoft.Extensions.Logging;

namespace HumanLayerAutomation.Providers;

/// <summary>
/// OpenAI API provider for GPT models.
/// </summary>
public class OpenAIProvider : ICodeProvider
{
    private readonly HttpClient _httpClient;
    private readonly ModelRegistry _modelRegistry;
    private readonly QuotaManager _quotaManager;
    private readonly ILogger? _logger;
    private readonly OpenAIProviderOptions _options;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string ProviderId => "openai";

    public OpenAIProvider(
        ModelRegistry modelRegistry,
        QuotaManager quotaManager,
        OpenAIProviderOptions? options = null,
        ILogger? logger = null)
    {
        _modelRegistry = modelRegistry;
        _quotaManager = quotaManager;
        _options = options ?? new OpenAIProviderOptions();
        _logger = logger;

        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(_options.BaseUrl),
            Timeout = TimeSpan.FromMinutes(5)
        };

        if (!string.IsNullOrEmpty(_options.ApiKey))
        {
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_options.ApiKey}");
        }
    }

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_options.ApiKey))
        {
            _logger?.LogDebug("OpenAI API key not configured");
            return false;
        }

        try
        {
            // Quick check with models endpoint
            var response = await _httpClient.GetAsync("models", ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "OpenAI availability check failed");
            return false;
        }
    }

    public async Task<ProviderResult> RunAsync(ProviderRequest request, CancellationToken ct = default)
    {
        var startTime = DateTime.UtcNow;
        var result = new ProviderResult { ProviderId = ProviderId };

        try
        {
            // Check quota
            var quotaCheck = _quotaManager.CheckQuota(ProviderId);
            if (!quotaCheck.IsAvailable)
            {
                result.Success = false;
                result.Error = quotaCheck.Reason;
                result.QuotaExhausted = true;
                return result;
            }

            // Resolve model
            var modelAlias = request.Model ?? _options.DefaultModel;
            var modelInfo = _modelRegistry.GetModelByAlias(modelAlias);
            var modelId = modelInfo?.Id ?? modelAlias ?? "gpt-4o";

            result.ModelId = modelId;

            // Build request
            var messages = new List<OpenAIMessage>();

            if (!string.IsNullOrEmpty(request.SystemPrompt))
            {
                messages.Add(new OpenAIMessage { Role = "system", Content = request.SystemPrompt });
            }

            messages.Add(new OpenAIMessage { Role = "user", Content = request.Prompt });

            var chatRequest = new OpenAIChatRequest
            {
                Model = modelId,
                Messages = messages,
                MaxTokens = request.MaxTokens ?? 4096,
                Temperature = 0.1 // Low temperature for coding tasks
            };

            var jsonContent = JsonSerializer.Serialize(chatRequest, JsonOptions);
            var httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            _logger?.LogInformation("Calling OpenAI: {Model}", modelId);

            var response = await _httpClient.PostAsync("chat/completions", httpContent, ct);
            var responseJson = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                result.Success = false;
                result.Error = $"OpenAI API error: {response.StatusCode} - {responseJson}";

                // Check for rate limit
                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    result.QuotaExhausted = true;
                }

                return result;
            }

            var chatResponse = JsonSerializer.Deserialize<OpenAIChatResponse>(responseJson, JsonOptions);

            if (chatResponse == null)
            {
                result.Success = false;
                result.Error = "Failed to parse OpenAI response";
                return result;
            }

            result.Success = true;
            result.Output = chatResponse.Choices?.FirstOrDefault()?.Message?.Content;
            result.InputTokens = chatResponse.Usage?.PromptTokens ?? 0;
            result.OutputTokens = chatResponse.Usage?.CompletionTokens ?? 0;
            result.Duration = DateTime.UtcNow - startTime;

            // Calculate cost
            if (modelInfo != null)
            {
                result.Cost = _modelRegistry.CalculateCost(modelId, result.InputTokens, result.OutputTokens);
            }

            // Record usage
            _modelRegistry.RecordUsage(modelId, result.InputTokens, result.OutputTokens, result.Cost);
            _quotaManager.RecordUsage(ProviderId, result.InputTokens, result.OutputTokens);
        }
        catch (TaskCanceledException)
        {
            result.Success = false;
            result.Error = "Request timed out";
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
            _logger?.LogError(ex, "OpenAI provider error");
        }

        result.Duration = DateTime.UtcNow - startTime;
        return result;
    }

    public Task<QuotaInfo?> GetQuotaAsync(CancellationToken ct = default)
    {
        var metrics = _quotaManager.GetMetrics(ProviderId);

        return Task.FromResult<QuotaInfo?>(new QuotaInfo
        {
            IsUnlimited = metrics.BudgetLimit <= 0,
            RemainingBudget = metrics.BudgetLimit > 0 ? metrics.BudgetLimit - metrics.CurrentMonthSpend : null
        });
    }
}

public class OpenAIProviderOptions
{
    public string BaseUrl { get; set; } = "https://api.openai.com/v1/";
    public string? ApiKey { get; set; } = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
    public string DefaultModel { get; set; } = "gpt-4o";
}

// OpenAI API models
internal class OpenAIChatRequest
{
    public string Model { get; set; } = "";
    public List<OpenAIMessage> Messages { get; set; } = [];
    public int? MaxTokens { get; set; }
    public double Temperature { get; set; } = 0.7;
}

internal class OpenAIMessage
{
    public string Role { get; set; } = "";
    public string Content { get; set; } = "";
}

internal class OpenAIChatResponse
{
    public string? Id { get; set; }
    public List<OpenAIChoice>? Choices { get; set; }
    public OpenAIUsage? Usage { get; set; }
}

internal class OpenAIChoice
{
    public OpenAIMessage? Message { get; set; }
    public string? FinishReason { get; set; }
}

internal class OpenAIUsage
{
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public int TotalTokens { get; set; }
}
