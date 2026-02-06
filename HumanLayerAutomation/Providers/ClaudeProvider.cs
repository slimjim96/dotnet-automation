using System.Text.Json;
using HumanLayerAutomation.Models;
using Microsoft.Extensions.Logging;

namespace HumanLayerAutomation.Providers;

/// <summary>
/// Claude Code CLI provider.
/// </summary>
public class ClaudeProvider : ICodeProvider
{
    private readonly ClaudeCodeClient _client;
    private readonly ModelRegistry _modelRegistry;
    private readonly QuotaManager _quotaManager;
    private readonly ILogger? _logger;
    private readonly ClaudeProviderOptions _options;

    public string ProviderId => "anthropic";

    public ClaudeProvider(
        ClaudeCodeClient client,
        ModelRegistry modelRegistry,
        QuotaManager? quotaManager = null,
        ClaudeProviderOptions? options = null,
        ILogger? logger = null)
    {
        _client = client;
        _modelRegistry = modelRegistry;
        _quotaManager = quotaManager ?? new QuotaManager();
        _options = options ?? new ClaudeProviderOptions();
        _logger = logger;
    }

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            return await _client.IsAvailableAsync(ct);
        }
        catch
        {
            return false;
        }
    }

    public async Task<ProviderResult> RunAsync(ProviderRequest request, CancellationToken ct = default)
    {
        var startTime = DateTime.UtcNow;
        var result = new ProviderResult { ProviderId = ProviderId };

        try
        {
            // Check quota first
            var quotaCheck = _quotaManager.CheckQuota(ProviderId);
            if (!quotaCheck.IsAvailable)
            {
                result.Success = false;
                result.Error = quotaCheck.Reason;
                result.QuotaExhausted = true;

                // Include time until reset for caller to decide whether to wait
                if (quotaCheck.TimeUntilReset.HasValue)
                {
                    result.Error += $" (resets in {quotaCheck.TimeUntilReset.Value:hh\\:mm\\:ss})";
                }

                return result;
            }

            // Resolve model alias to actual model ID
            var modelAlias = request.Model ?? _options.DefaultModel ?? "sonnet";
            var modelInfo = _modelRegistry.GetModelByAlias(modelAlias);
            var modelId = modelInfo?.Id ?? modelAlias;

            result.ModelId = modelId;

            var maxTurns = request.MaxTokens.HasValue
                ? Math.Max(1, request.MaxTokens.Value / 1000)
                : (int?)null;

            var options = new ClaudeOptions
            {
                Model = modelAlias, // Claude CLI accepts aliases
                WorkingDir = request.WorkingDirectory ?? _client.DefaultWorkingDir,
                AutoApprove = request.AutoApprove,
                AllowedTools = request.AllowedTools,
                SystemPrompt = request.SystemPrompt,
                OutputFormat = "json",
                MaxTurns = maxTurns
            };

            var claudeResult = await _client.RunAsync(request.Prompt, options, ct);

            result.Success = claudeResult.Success;
            result.Output = claudeResult.Output;
            result.Error = claudeResult.Error;
            result.InputTokens = claudeResult.InputTokens ?? 0;
            result.OutputTokens = claudeResult.OutputTokens ?? 0;
            result.Duration = DateTime.UtcNow - startTime;

            // Calculate cost
            if (modelInfo != null)
            {
                result.Cost = _modelRegistry.CalculateCost(modelId, result.InputTokens, result.OutputTokens);
            }
            else
            {
                result.Cost = claudeResult.CostUsd ?? 0;
            }

            // Record usage in both model registry and quota manager
            _modelRegistry.RecordUsage(modelId, result.InputTokens, result.OutputTokens, result.Cost);
            _quotaManager.RecordUsage(ProviderId, result.InputTokens, result.OutputTokens);

            // Check for quota exhaustion indicators
            if (claudeResult.Error?.Contains("rate limit", StringComparison.OrdinalIgnoreCase) == true ||
                claudeResult.Error?.Contains("quota", StringComparison.OrdinalIgnoreCase) == true ||
                claudeResult.Error?.Contains("limit", StringComparison.OrdinalIgnoreCase) == true)
            {
                result.QuotaExhausted = true;
            }
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
            result.Duration = DateTime.UtcNow - startTime;
            _logger?.LogError(ex, "Claude provider error");
        }

        return result;
    }

    public Task<QuotaInfo?> GetQuotaAsync(CancellationToken ct = default)
    {
        var metrics = _quotaManager.GetMetrics(ProviderId);
        var timeUntilReset = _quotaManager.GetTimeUntilReset(ProviderId);

        return Task.FromResult<QuotaInfo?>(new QuotaInfo
        {
            IsUnlimited = false,
            RemainingTokens = metrics.WindowLimit - metrics.CurrentWindowUsage,
            ResetsAt = timeUntilReset.HasValue ? DateTime.UtcNow.Add(timeUntilReset.Value) : null
        });
    }

    /// <summary>Get time until quota resets.</summary>
    public TimeSpan? GetTimeUntilReset() => _quotaManager.GetTimeUntilReset(ProviderId);
}

public class ClaudeProviderOptions
{
    public string? DefaultModel { get; set; } = "sonnet";
    public decimal MaxBudgetUsd { get; set; } = 0; // 0 = unlimited
    public DateTime? BudgetPeriodStart { get; set; }
}
