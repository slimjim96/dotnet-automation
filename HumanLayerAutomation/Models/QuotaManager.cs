using System.Text.Json;
using System.Text.Json.Serialization;

namespace HumanLayerAutomation.Models;

/// <summary>
/// Manages quotas for different billing models:
/// - Time-based resets (Claude Pro - 5 hour windows)
/// - Monthly limits with pacing (GitHub Copilot Pro)
/// - Pay-per-use (OpenAI API)
/// </summary>
public class QuotaManager
{
    private readonly string _quotaPath;
    private QuotaData _data;
    private static readonly object _lock = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public QuotaManager(string? quotaPath = null)
    {
        _quotaPath = quotaPath ?? GetDefaultPath();
        _data = Load();

        // Initialize defaults if empty
        if (_data.ProviderQuotas.Count == 0)
        {
            InitializeDefaults();
        }
    }

    /// <summary>Get quota config for a provider.</summary>
    public ProviderQuota? GetProviderQuota(string providerId) =>
        _data.ProviderQuotas.FirstOrDefault(q => q.ProviderId.Equals(providerId, StringComparison.OrdinalIgnoreCase));

    /// <summary>Check if a provider has available quota.</summary>
    public QuotaCheckResult CheckQuota(string providerId, int estimatedTokens = 1000)
    {
        var quota = GetProviderQuota(providerId);
        if (quota == null || !quota.Enabled)
        {
            return new QuotaCheckResult
            {
                IsAvailable = false,
                Reason = "Provider not configured or disabled"
            };
        }

        return quota.BillingModel switch
        {
            BillingModel.TimeBasedReset => CheckTimeBasedQuota(quota, estimatedTokens),
            BillingModel.MonthlyLimit => CheckMonthlyQuota(quota, estimatedTokens),
            BillingModel.PayPerUse => CheckPayPerUseQuota(quota, estimatedTokens),
            BillingModel.Unlimited => new QuotaCheckResult { IsAvailable = true },
            _ => new QuotaCheckResult { IsAvailable = true }
        };
    }

    /// <summary>Record usage for a provider.</summary>
    public void RecordUsage(string providerId, int inputTokens, int outputTokens, int premiumPrompts = 1)
    {
        lock (_lock)
        {
            var quota = GetProviderQuota(providerId);
            if (quota == null) return;

            var usage = new UsageRecord
            {
                ProviderId = providerId,
                Timestamp = DateTime.UtcNow,
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                PremiumPrompts = premiumPrompts
            };

            _data.UsageHistory.Add(usage);

            // Update current window/period usage
            UpdateCurrentUsage(quota, inputTokens, outputTokens, premiumPrompts);

            // Keep only last 30 days of history
            var cutoff = DateTime.UtcNow.AddDays(-30);
            _data.UsageHistory.RemoveAll(u => u.Timestamp < cutoff);

            Save();
        }
    }

    /// <summary>Get time until quota resets for time-based providers.</summary>
    public TimeSpan? GetTimeUntilReset(string providerId)
    {
        var quota = GetProviderQuota(providerId);
        if (quota?.BillingModel != BillingModel.TimeBasedReset)
            return null;

        var windowStart = quota.CurrentWindowStart ?? DateTime.UtcNow;
        var windowEnd = windowStart.AddHours(quota.ResetIntervalHours);

        if (DateTime.UtcNow >= windowEnd)
            return TimeSpan.Zero; // Already reset

        return windowEnd - DateTime.UtcNow;
    }

    /// <summary>Get recommended daily budget for monthly-limited providers.</summary>
    public int GetDailyBudget(string providerId)
    {
        var quota = GetProviderQuota(providerId);
        if (quota?.BillingModel != BillingModel.MonthlyLimit)
            return int.MaxValue;

        var daysRemaining = GetDaysRemainingInMonth();
        var remaining = quota.MonthlyLimit - quota.CurrentMonthUsage;

        return Math.Max(1, remaining / Math.Max(1, daysRemaining));
    }

    /// <summary>Check if a task would exceed recommended pacing.</summary>
    public bool WouldExceedPacing(string providerId, int estimatedPrompts)
    {
        var quota = GetProviderQuota(providerId);
        if (quota?.BillingModel != BillingModel.MonthlyLimit)
            return false;

        var dailyBudget = GetDailyBudget(providerId);
        var usedToday = GetUsageToday(providerId);

        return (usedToday + estimatedPrompts) > dailyBudget * quota.PacingMultiplier;
    }

    /// <summary>Update provider quota configuration.</summary>
    public void UpdateProviderQuota(ProviderQuota quota)
    {
        lock (_lock)
        {
            var existing = _data.ProviderQuotas.FindIndex(q =>
                q.ProviderId.Equals(quota.ProviderId, StringComparison.OrdinalIgnoreCase));

            if (existing >= 0)
                _data.ProviderQuotas[existing] = quota;
            else
                _data.ProviderQuotas.Add(quota);

            Save();
        }
    }

    /// <summary>Get usage metrics for a provider.</summary>
    public ProviderMetrics GetMetrics(string providerId)
    {
        var quota = GetProviderQuota(providerId);
        var metrics = new ProviderMetrics { ProviderId = providerId };

        if (quota == null) return metrics;

        metrics.BillingModel = quota.BillingModel;

        switch (quota.BillingModel)
        {
            case BillingModel.TimeBasedReset:
                metrics.CurrentWindowUsage = quota.CurrentWindowTokens;
                metrics.WindowLimit = quota.TokensPerWindow;
                metrics.TimeUntilReset = GetTimeUntilReset(providerId);
                metrics.WindowStartTime = quota.CurrentWindowStart;
                break;

            case BillingModel.MonthlyLimit:
                metrics.CurrentMonthUsage = quota.CurrentMonthUsage;
                metrics.MonthlyLimit = quota.MonthlyLimit;
                metrics.DailyBudget = GetDailyBudget(providerId);
                metrics.UsedToday = GetUsageToday(providerId);
                metrics.DaysRemaining = GetDaysRemainingInMonth();
                break;

            case BillingModel.PayPerUse:
                metrics.CurrentMonthSpend = GetMonthlySpend(providerId);
                metrics.BudgetLimit = quota.MonthlyBudgetUsd;
                break;
        }

        return metrics;
    }

    /// <summary>Get all provider metrics.</summary>
    public List<ProviderMetrics> GetAllMetrics() =>
        _data.ProviderQuotas.Select(q => GetMetrics(q.ProviderId)).ToList();

    /// <summary>Reset a time-based window (for testing or manual reset).</summary>
    public void ResetWindow(string providerId)
    {
        lock (_lock)
        {
            var quota = GetProviderQuota(providerId);
            if (quota == null) return;

            quota.CurrentWindowStart = DateTime.UtcNow;
            quota.CurrentWindowTokens = 0;
            Save();
        }
    }

    /// <summary>Reset monthly usage (for new billing cycle).</summary>
    public void ResetMonthlyUsage(string providerId)
    {
        lock (_lock)
        {
            var quota = GetProviderQuota(providerId);
            if (quota == null) return;

            quota.CurrentMonthUsage = 0;
            quota.CurrentMonthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
            Save();
        }
    }

    // ========================================================================
    // Private Methods
    // ========================================================================

    private QuotaCheckResult CheckTimeBasedQuota(ProviderQuota quota, int estimatedTokens)
    {
        // Check if window has reset
        if (quota.CurrentWindowStart.HasValue)
        {
            var windowEnd = quota.CurrentWindowStart.Value.AddHours(quota.ResetIntervalHours);
            if (DateTime.UtcNow >= windowEnd)
            {
                // Window has reset
                quota.CurrentWindowStart = DateTime.UtcNow;
                quota.CurrentWindowTokens = 0;
                Save();
            }
        }
        else
        {
            quota.CurrentWindowStart = DateTime.UtcNow;
            quota.CurrentWindowTokens = 0;
            Save();
        }

        var remaining = quota.TokensPerWindow - quota.CurrentWindowTokens;
        var available = remaining >= estimatedTokens;

        return new QuotaCheckResult
        {
            IsAvailable = available,
            RemainingTokens = remaining,
            TimeUntilReset = available ? null : GetTimeUntilReset(quota.ProviderId),
            Reason = available ? null : $"Window quota exhausted. Resets in {GetTimeUntilReset(quota.ProviderId):hh\\:mm\\:ss}",
            CanWaitForReset = true
        };
    }

    private QuotaCheckResult CheckMonthlyQuota(ProviderQuota quota, int estimatedPrompts)
    {
        // Check if month has reset
        var currentMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        if (!quota.CurrentMonthStart.HasValue || quota.CurrentMonthStart.Value < currentMonth)
        {
            quota.CurrentMonthStart = currentMonth;
            quota.CurrentMonthUsage = 0;
            Save();
        }

        var remaining = quota.MonthlyLimit - quota.CurrentMonthUsage;
        var dailyBudget = GetDailyBudget(quota.ProviderId);
        var usedToday = GetUsageToday(quota.ProviderId);

        // Check absolute limit
        if (remaining < estimatedPrompts)
        {
            return new QuotaCheckResult
            {
                IsAvailable = false,
                RemainingPrompts = remaining,
                Reason = $"Monthly limit reached ({quota.CurrentMonthUsage}/{quota.MonthlyLimit})",
                CanWaitForReset = false // Monthly reset is too long
            };
        }

        // Check pacing
        var wouldExceedPacing = (usedToday + estimatedPrompts) > dailyBudget * quota.PacingMultiplier;
        if (wouldExceedPacing && quota.EnforcePacing)
        {
            return new QuotaCheckResult
            {
                IsAvailable = false,
                RemainingPrompts = remaining,
                DailyBudgetRemaining = dailyBudget - usedToday,
                Reason = $"Would exceed daily pacing ({usedToday + estimatedPrompts} > {dailyBudget} daily budget)",
                RecommendWait = true
            };
        }

        return new QuotaCheckResult
        {
            IsAvailable = true,
            RemainingPrompts = remaining,
            DailyBudgetRemaining = dailyBudget - usedToday,
            PacingWarning = wouldExceedPacing ? "Exceeding recommended daily pace" : null
        };
    }

    private QuotaCheckResult CheckPayPerUseQuota(ProviderQuota quota, int estimatedTokens)
    {
        if (quota.MonthlyBudgetUsd <= 0)
        {
            return new QuotaCheckResult { IsAvailable = true }; // No budget limit
        }

        var spent = GetMonthlySpend(quota.ProviderId);
        var remaining = quota.MonthlyBudgetUsd - spent;

        // Estimate cost (rough, actual cost calculated after)
        var estimatedCost = (estimatedTokens / 1_000_000m) * 10m; // Assume $10/M as rough estimate

        return new QuotaCheckResult
        {
            IsAvailable = remaining > estimatedCost,
            RemainingBudget = remaining,
            Reason = remaining <= estimatedCost ? $"Budget limit reached (${spent:F2}/${quota.MonthlyBudgetUsd:F2})" : null
        };
    }

    private void UpdateCurrentUsage(ProviderQuota quota, int inputTokens, int outputTokens, int premiumPrompts)
    {
        switch (quota.BillingModel)
        {
            case BillingModel.TimeBasedReset:
                quota.CurrentWindowTokens += inputTokens + outputTokens;
                break;

            case BillingModel.MonthlyLimit:
                quota.CurrentMonthUsage += premiumPrompts;
                break;
        }
    }

    private int GetUsageToday(string providerId)
    {
        var today = DateTime.UtcNow.Date;
        return _data.UsageHistory
            .Where(u => u.ProviderId == providerId && u.Timestamp.Date == today)
            .Sum(u => u.PremiumPrompts);
    }

    private decimal GetMonthlySpend(string providerId)
    {
        var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        // This would need to be calculated based on actual costs - placeholder
        return 0m;
    }

    private int GetDaysRemainingInMonth()
    {
        var today = DateTime.UtcNow;
        var lastDay = DateTime.DaysInMonth(today.Year, today.Month);
        return lastDay - today.Day + 1;
    }

    private void InitializeDefaults()
    {
        _data.ProviderQuotas =
        [
            // Claude Pro - 5 hour reset window
            new ProviderQuota
            {
                ProviderId = "anthropic",
                DisplayName = "Claude Pro",
                BillingModel = BillingModel.TimeBasedReset,
                ResetIntervalHours = 5,
                TokensPerWindow = 500000, // Approximate, adjust based on plan
                Enabled = true,
                Notes = "Resets every 5 hours. Good for background tasks that can wait."
            },

            // GitHub Copilot Pro - Monthly premium prompts
            new ProviderQuota
            {
                ProviderId = "github",
                DisplayName = "GitHub Copilot Pro",
                BillingModel = BillingModel.MonthlyLimit,
                MonthlyLimit = 300, // Premium prompts per month (adjust based on plan)
                PacingMultiplier = 1.5m, // Allow 50% burst above daily average
                EnforcePacing = true,
                Enabled = true,
                Notes = "Monthly premium prompt limit. Pacing prevents single tasks from consuming too much."
            },

            // OpenAI - Pay per use
            new ProviderQuota
            {
                ProviderId = "openai",
                DisplayName = "OpenAI API",
                BillingModel = BillingModel.PayPerUse,
                MonthlyBudgetUsd = 50m, // Optional monthly budget
                Enabled = false, // Disabled by default
                Notes = "Pay-per-token. Set monthly budget to control costs."
            }
        ];

        Save();
    }

    private QuotaData Load()
    {
        if (!File.Exists(_quotaPath))
            return new QuotaData();

        try
        {
            var json = File.ReadAllText(_quotaPath);
            return JsonSerializer.Deserialize<QuotaData>(json, JsonOptions) ?? new QuotaData();
        }
        catch
        {
            return new QuotaData();
        }
    }

    private void Save()
    {
        var dir = Path.GetDirectoryName(_quotaPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(_data, JsonOptions);
        File.WriteAllText(_quotaPath, json);
    }

    private static string GetDefaultPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "dotnet-automation", "quota-manager.json");
    }

    private class QuotaData
    {
        public List<ProviderQuota> ProviderQuotas { get; set; } = [];
        public List<UsageRecord> UsageHistory { get; set; } = [];
    }
}

/// <summary>
/// Quota configuration for a provider.
/// </summary>
public class ProviderQuota
{
    public string ProviderId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public BillingModel BillingModel { get; set; }
    public bool Enabled { get; set; } = true;
    public string? Notes { get; set; }

    // Time-based reset (Claude Pro)
    public int ResetIntervalHours { get; set; } = 5;
    public int TokensPerWindow { get; set; }
    public DateTime? CurrentWindowStart { get; set; }
    public int CurrentWindowTokens { get; set; }

    // Monthly limit (GitHub Copilot)
    public int MonthlyLimit { get; set; }
    public int CurrentMonthUsage { get; set; }
    public DateTime? CurrentMonthStart { get; set; }
    public decimal PacingMultiplier { get; set; } = 1.0m;
    public bool EnforcePacing { get; set; } = true;

    // Pay per use (OpenAI)
    public decimal MonthlyBudgetUsd { get; set; }
}

public enum BillingModel
{
    /// <summary>Unlimited usage (rare).</summary>
    Unlimited,

    /// <summary>Quota resets after time period (Claude Pro).</summary>
    TimeBasedReset,

    /// <summary>Fixed monthly limit (GitHub Copilot Pro).</summary>
    MonthlyLimit,

    /// <summary>Pay per token/request (OpenAI API).</summary>
    PayPerUse
}

/// <summary>
/// Result of a quota check.
/// </summary>
public class QuotaCheckResult
{
    public bool IsAvailable { get; set; }
    public string? Reason { get; set; }

    // Time-based
    public int RemainingTokens { get; set; }
    public TimeSpan? TimeUntilReset { get; set; }
    public bool CanWaitForReset { get; set; }

    // Monthly limit
    public int RemainingPrompts { get; set; }
    public int DailyBudgetRemaining { get; set; }
    public string? PacingWarning { get; set; }
    public bool RecommendWait { get; set; }

    // Pay per use
    public decimal RemainingBudget { get; set; }
}

/// <summary>
/// Metrics for a provider's usage.
/// </summary>
public class ProviderMetrics
{
    public string ProviderId { get; set; } = "";
    public BillingModel BillingModel { get; set; }

    // Time-based
    public int CurrentWindowUsage { get; set; }
    public int WindowLimit { get; set; }
    public TimeSpan? TimeUntilReset { get; set; }
    public DateTime? WindowStartTime { get; set; }

    // Monthly
    public int CurrentMonthUsage { get; set; }
    public int MonthlyLimit { get; set; }
    public int DailyBudget { get; set; }
    public int UsedToday { get; set; }
    public int DaysRemaining { get; set; }

    // Pay per use
    public decimal CurrentMonthSpend { get; set; }
    public decimal BudgetLimit { get; set; }
}

/// <summary>
/// Individual usage record.
/// </summary>
public class UsageRecord
{
    public string ProviderId { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public int PremiumPrompts { get; set; } = 1;
}
