using HumanLayerAutomation.Models;
using Microsoft.Extensions.Logging;

namespace HumanLayerAutomation.Providers;

/// <summary>
/// Multi-provider manager with automatic fallback support.
/// Tries providers in order until one succeeds or all fail.
/// Supports waiting for quota reset on time-based providers.
/// </summary>
public class MultiProvider : ICodeProvider
{
    private readonly List<ICodeProvider> _providers;
    private readonly ModelRegistry _modelRegistry;
    private readonly QuotaManager _quotaManager;
    private readonly ILogger? _logger;
    private readonly MultiProviderOptions _options;

    public string ProviderId => "multi";

    public MultiProvider(
        IEnumerable<ICodeProvider> providers,
        ModelRegistry modelRegistry,
        QuotaManager? quotaManager = null,
        MultiProviderOptions? options = null,
        ILogger? logger = null)
    {
        _providers = providers.ToList();
        _modelRegistry = modelRegistry;
        _quotaManager = quotaManager ?? new QuotaManager();
        _options = options ?? new MultiProviderOptions();
        _logger = logger;
    }

    /// <summary>Create a multi-provider with all available providers.</summary>
    public static MultiProvider CreateDefault(
        ClaudeCodeClient claudeClient,
        ModelRegistry modelRegistry,
        QuotaManager? quotaManager = null,
        ILoggerFactory? loggerFactory = null)
    {
        quotaManager ??= new QuotaManager();

        var providers = new List<ICodeProvider>
        {
            new ClaudeProvider(
                claudeClient,
                modelRegistry,
                quotaManager,
                logger: loggerFactory?.CreateLogger<ClaudeProvider>()),
            new GitHubCopilotProvider(
                modelRegistry,
                quotaManager,
                logger: loggerFactory?.CreateLogger<GitHubCopilotProvider>()),
            new OpenAIProvider(
                modelRegistry,
                quotaManager,
                logger: loggerFactory?.CreateLogger<OpenAIProvider>())
        };

        return new MultiProvider(
            providers,
            modelRegistry,
            quotaManager,
            logger: loggerFactory?.CreateLogger<MultiProvider>());
    }

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        foreach (var provider in _providers)
        {
            if (await provider.IsAvailableAsync(ct))
                return true;
        }
        return false;
    }

    public async Task<ProviderResult> RunAsync(ProviderRequest request, CancellationToken ct = default)
    {
        var errors = new List<string>();
        var totalCost = 0m;
        ClaudeProvider? claudeProviderForWaiting = null;

        foreach (var provider in _providers)
        {
            // Check if provider is available
            if (!await provider.IsAvailableAsync(ct))
            {
                _logger?.LogDebug("Skipping unavailable provider: {Provider}", provider.ProviderId);
                continue;
            }

            // Check quota before trying
            var quotaCheck = _quotaManager.CheckQuota(provider.ProviderId);
            if (!quotaCheck.IsAvailable)
            {
                _logger?.LogInformation("Provider {Provider} quota unavailable: {Reason}",
                    provider.ProviderId, quotaCheck.Reason);

                // If Claude can wait for reset, remember it
                if (provider is ClaudeProvider cp && quotaCheck.CanWaitForReset)
                {
                    claudeProviderForWaiting = cp;
                }

                // Check pacing for monthly-limited providers
                if (quotaCheck.RecommendWait && !_options.IgnorePacing)
                {
                    _logger?.LogInformation("Skipping {Provider} due to pacing recommendation", provider.ProviderId);
                    continue;
                }

                if (!quotaCheck.IsAvailable)
                    continue;
            }

            _logger?.LogInformation("Trying provider: {Provider}", provider.ProviderId);

            var result = await provider.RunAsync(request, ct);
            totalCost += result.Cost;

            if (result.Success)
            {
                result.Cost = totalCost; // Include cost from failed attempts
                return result;
            }

            // Record the error
            var errorMsg = $"{provider.ProviderId}: {result.Error}";
            errors.Add(errorMsg);
            _logger?.LogWarning("Provider {Provider} failed: {Error}", provider.ProviderId, result.Error);

            // Check if we should fall back
            if (!ShouldFallback(result))
            {
                _logger?.LogInformation("Not falling back - error is not recoverable");
                break;
            }

            // If quota exhausted, definitely try next provider
            if (result.QuotaExhausted)
            {
                _logger?.LogInformation("Quota exhausted, trying next provider");

                // Remember Claude for waiting if it has time-based reset
                if (provider is ClaudeProvider cp2)
                {
                    claudeProviderForWaiting = cp2;
                }
                continue;
            }

            // Check retry delay
            if (_options.FallbackDelayMs > 0)
            {
                await Task.Delay(_options.FallbackDelayMs, ct);
            }
        }

        // All providers failed - should we wait for Claude reset?
        if (_options.WaitForQuotaReset && claudeProviderForWaiting != null)
        {
            var timeUntilReset = claudeProviderForWaiting.GetTimeUntilReset();
            if (timeUntilReset.HasValue && timeUntilReset.Value <= _options.MaxWaitTime)
            {
                _logger?.LogInformation("All providers exhausted. Waiting {Time} for Claude quota reset...",
                    timeUntilReset.Value);

                await Task.Delay(timeUntilReset.Value + TimeSpan.FromSeconds(5), ct); // Small buffer

                // Try Claude again after reset
                var retryResult = await claudeProviderForWaiting.RunAsync(request, ct);
                retryResult.Cost += totalCost;
                return retryResult;
            }
        }

        // All providers failed
        return new ProviderResult
        {
            Success = false,
            ProviderId = ProviderId,
            Error = $"All providers failed:\n{string.Join("\n", errors)}",
            Cost = totalCost
        };
    }

    public async Task<QuotaInfo?> GetQuotaAsync(CancellationToken ct = default)
    {
        // Return combined quota info
        var totalBudget = 0m;
        var unlimited = false;

        foreach (var provider in _providers)
        {
            var quota = await provider.GetQuotaAsync(ct);
            if (quota == null) continue;

            if (quota.IsUnlimited)
            {
                unlimited = true;
            }
            else if (quota.RemainingBudget.HasValue)
            {
                totalBudget += quota.RemainingBudget.Value;
            }
        }

        return new QuotaInfo
        {
            IsUnlimited = unlimited,
            RemainingBudget = unlimited ? null : totalBudget
        };
    }

    /// <summary>Get the list of available providers.</summary>
    public async Task<List<(string ProviderId, bool Available)>> GetProviderStatusAsync(CancellationToken ct = default)
    {
        var status = new List<(string, bool)>();
        foreach (var provider in _providers)
        {
            var available = await provider.IsAvailableAsync(ct);
            status.Add((provider.ProviderId, available));
        }
        return status;
    }

    /// <summary>Add a provider to the fallback chain.</summary>
    public void AddProvider(ICodeProvider provider)
    {
        _providers.Add(provider);
    }

    /// <summary>Remove a provider from the fallback chain.</summary>
    public void RemoveProvider(string providerId)
    {
        _providers.RemoveAll(p => p.ProviderId == providerId);
    }

    /// <summary>Reorder providers by priority.</summary>
    public void SetProviderOrder(IEnumerable<string> providerIds)
    {
        var ordered = new List<ICodeProvider>();
        foreach (var id in providerIds)
        {
            var provider = _providers.FirstOrDefault(p => p.ProviderId == id);
            if (provider != null)
                ordered.Add(provider);
        }
        // Add any remaining providers
        ordered.AddRange(_providers.Where(p => !ordered.Contains(p)));
        _providers.Clear();
        _providers.AddRange(ordered);
    }

    private bool ShouldFallback(ProviderResult result)
    {
        if (result.QuotaExhausted)
            return true;

        var error = result.Error?.ToLower() ?? "";

        // Fall back on transient/network errors
        if (error.Contains("timeout") ||
            error.Contains("rate limit") ||
            error.Contains("service unavailable") ||
            error.Contains("503") ||
            error.Contains("429") ||
            error.Contains("502") ||
            error.Contains("504") ||
            error.Contains("connection") ||
            error.Contains("network"))
        {
            return true;
        }

        // Don't fall back on authentication errors — need to fix config
        if (error.Contains("unauthorized") ||
            error.Contains("authentication") ||
            error.Contains("api key") ||
            error.Contains("403") ||
            error.Contains("invalid token"))
        {
            return false;
        }

        // Don't fall back on build/code errors — these should be fed back
        // to the SAME provider for self-healing, not retried on a different one
        if (error.Contains("build failed") ||
            error.Contains("compile error") ||
            error.Contains("syntax error"))
        {
            return false;
        }

        // Default: fall back for autonomous operation
        return _options.FallbackOnAnyError;
    }
}

public class MultiProviderOptions
{
    /// <summary>Check provider quota before attempting.</summary>
    public bool CheckQuotaBeforeTrying { get; set; } = true;

    /// <summary>Delay in ms before trying next provider.</summary>
    public int FallbackDelayMs { get; set; } = 0;

    /// <summary>Fall back on any error (not just quota/transient).</summary>
    public bool FallbackOnAnyError { get; set; } = true;

    /// <summary>Wait for Claude quota reset if all providers exhausted.</summary>
    public bool WaitForQuotaReset { get; set; } = true;

    /// <summary>Maximum time to wait for quota reset.</summary>
    public TimeSpan MaxWaitTime { get; set; } = TimeSpan.FromHours(5);

    /// <summary>
    /// Ignore pacing recommendations for monthly-limited providers.
    /// Default true for autonomous operation — let builds run freely.
    /// </summary>
    public bool IgnorePacing { get; set; } = true;

    /// <summary>Maximum percentage of monthly quota a single task can use.</summary>
    public int MaxSingleTaskPercentage { get; set; } = 25;
}
