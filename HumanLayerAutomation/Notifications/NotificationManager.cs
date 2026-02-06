using System.Text.Json;
using System.Text.Json.Serialization;
using HumanLayerAutomation.Models;
using Microsoft.Extensions.Logging;

namespace HumanLayerAutomation.Notifications;

/// <summary>
/// Manages notification channels and sends alerts for quota warnings and task completion.
/// </summary>
public class NotificationManager
{
    private readonly List<INotificationChannel> _channels = [];
    private readonly NotificationConfig _config;
    private readonly ILogger? _logger;
    private readonly string _configPath;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public NotificationManager(ILogger? logger = null)
    {
        _configPath = GetDefaultConfigPath();
        _config = LoadConfig();
        _logger = logger;

        InitializeChannels();
    }

    /// <summary>Get all configured channels.</summary>
    public IReadOnlyList<INotificationChannel> Channels => _channels.AsReadOnly();

    /// <summary>Get enabled channel IDs.</summary>
    public IReadOnlyList<string> EnabledChannels => _config.EnabledChannels.AsReadOnly();

    /// <summary>Send a notification to all enabled channels.</summary>
    public async Task<NotificationResult> SendAsync(Notification notification, CancellationToken ct = default)
    {
        var result = new NotificationResult { Notification = notification };

        // Check if this notification type is enabled
        if (!ShouldSend(notification.Type))
        {
            _logger?.LogDebug("Notification type {Type} is disabled", notification.Type);
            return result;
        }

        var enabledChannels = _channels
            .Where(c => c.IsConfigured && _config.EnabledChannels.Contains(c.ChannelId))
            .ToList();

        if (enabledChannels.Count == 0)
        {
            _logger?.LogDebug("No enabled notification channels");
            return result;
        }

        var tasks = enabledChannels.Select(async channel =>
        {
            try
            {
                var success = await channel.SendAsync(notification, ct);
                return (channel.ChannelId, success);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Channel {Channel} failed", channel.ChannelId);
                return (channel.ChannelId, false);
            }
        });

        var results = await Task.WhenAll(tasks);

        foreach (var (channelId, success) in results)
        {
            result.ChannelResults[channelId] = success;
        }

        result.Success = result.ChannelResults.Values.Any(v => v);
        return result;
    }

    /// <summary>Send a task completion notification.</summary>
    public async Task NotifyTaskCompletedAsync(string taskId, string taskName, bool success, TimeSpan duration, decimal cost, string? error = null)
    {
        var notification = new Notification
        {
            Title = success ? $"Task Completed: {taskName}" : $"Task Failed: {taskName}",
            Message = success
                ? $"Task '{taskName}' completed successfully in {duration.TotalSeconds:F1}s."
                : $"Task '{taskName}' failed: {error ?? "Unknown error"}",
            Level = success ? NotificationLevel.Success : NotificationLevel.Error,
            Type = success ? NotificationType.TaskCompleted : NotificationType.TaskFailed,
            Data = new Dictionary<string, string>
            {
                ["Task ID"] = taskId,
                ["Duration"] = $"{duration.TotalSeconds:F1}s",
                ["Cost"] = $"${cost:F4}"
            }
        };

        if (!string.IsNullOrEmpty(error))
        {
            notification.Data["Error"] = error;
        }

        await SendAsync(notification);
    }

    /// <summary>Send a quota warning notification.</summary>
    public async Task NotifyQuotaWarningAsync(string providerId, string providerName, int percentUsed, string details)
    {
        var notification = new Notification
        {
            Title = $"Quota Warning: {providerName}",
            Message = $"{providerName} is at {percentUsed}% of quota. {details}",
            Level = percentUsed >= 90 ? NotificationLevel.Error : NotificationLevel.Warning,
            Type = percentUsed >= 100 ? NotificationType.QuotaExhausted : NotificationType.QuotaWarning,
            Data = new Dictionary<string, string>
            {
                ["Provider"] = providerName,
                ["Usage"] = $"{percentUsed}%",
                ["Details"] = details
            }
        };

        await SendAsync(notification);
    }

    /// <summary>Send a quota reset notification.</summary>
    public async Task NotifyQuotaResetAsync(string providerId, string providerName)
    {
        var notification = new Notification
        {
            Title = $"Quota Reset: {providerName}",
            Message = $"{providerName} quota has reset. You can resume tasks.",
            Level = NotificationLevel.Success,
            Type = NotificationType.QuotaReset,
            Data = new Dictionary<string, string>
            {
                ["Provider"] = providerName,
                ["Time"] = DateTime.Now.ToString("HH:mm:ss")
            }
        };

        await SendAsync(notification);
    }

    /// <summary>Send a build completion notification.</summary>
    public async Task NotifyBuildCompletedAsync(string appName, bool success, TimeSpan duration, decimal cost, string outputPath, string? error = null)
    {
        var notification = new Notification
        {
            Title = success ? $"Build Succeeded: {appName}" : $"Build Failed: {appName}",
            Message = success
                ? $"Successfully built '{appName}' in {duration.TotalSeconds:F1}s"
                : $"Failed to build '{appName}': {error ?? "Unknown error"}",
            Level = success ? NotificationLevel.Success : NotificationLevel.Error,
            Type = success ? NotificationType.BuildSuccess : NotificationType.BuildFailed,
            Data = new Dictionary<string, string>
            {
                ["App"] = appName,
                ["Duration"] = $"{duration.TotalSeconds:F1}s",
                ["Cost"] = $"${cost:F4}",
                ["Output"] = outputPath
            }
        };

        await SendAsync(notification);
    }

    /// <summary>Enable a notification channel.</summary>
    public void EnableChannel(string channelId)
    {
        if (!_config.EnabledChannels.Contains(channelId))
        {
            _config.EnabledChannels.Add(channelId);
            SaveConfig();
        }
    }

    /// <summary>Disable a notification channel.</summary>
    public void DisableChannel(string channelId)
    {
        if (_config.EnabledChannels.Remove(channelId))
        {
            SaveConfig();
        }
    }

    /// <summary>Enable a notification type.</summary>
    public void EnableNotificationType(NotificationType type)
    {
        if (!_config.EnabledTypes.Contains(type))
        {
            _config.EnabledTypes.Add(type);
            SaveConfig();
        }
    }

    /// <summary>Disable a notification type.</summary>
    public void DisableNotificationType(NotificationType type)
    {
        _config.EnabledTypes.Remove(type);
        SaveConfig();
    }

    /// <summary>Set quota warning threshold percentage.</summary>
    public void SetQuotaWarningThreshold(int percent)
    {
        _config.QuotaWarningThresholdPercent = Math.Clamp(percent, 0, 100);
        SaveConfig();
    }

    /// <summary>Test all configured channels.</summary>
    public async Task<Dictionary<string, bool>> TestAllChannelsAsync(CancellationToken ct = default)
    {
        var results = new Dictionary<string, bool>();

        foreach (var channel in _channels.Where(c => c.IsConfigured))
        {
            try
            {
                results[channel.ChannelId] = await channel.TestAsync(ct);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Test failed for {Channel}", channel.ChannelId);
                results[channel.ChannelId] = false;
            }
        }

        return results;
    }

    /// <summary>Check quotas and send warnings if thresholds are exceeded.</summary>
    public async Task CheckQuotasAndNotifyAsync(QuotaManager quotaManager)
    {
        var metrics = quotaManager.GetAllMetrics();

        foreach (var m in metrics)
        {
            var quota = quotaManager.GetProviderQuota(m.ProviderId);
            if (quota == null || !quota.Enabled) continue;

            int percentUsed = 0;
            string details = "";

            switch (quota.BillingModel)
            {
                case BillingModel.TimeBasedReset:
                    if (m.WindowLimit > 0)
                    {
                        percentUsed = (int)(m.CurrentWindowUsage * 100.0 / m.WindowLimit);
                        var timeLeft = quotaManager.GetTimeUntilReset(m.ProviderId);
                        details = $"Resets in {timeLeft:hh\\:mm\\:ss}";
                    }
                    break;

                case BillingModel.MonthlyLimit:
                    if (m.MonthlyLimit > 0)
                    {
                        percentUsed = (int)(m.CurrentMonthUsage * 100.0 / m.MonthlyLimit);
                        details = $"{m.MonthlyLimit - m.CurrentMonthUsage} prompts remaining this month";
                    }
                    break;

                case BillingModel.PayPerUse:
                    if (m.BudgetLimit > 0)
                    {
                        percentUsed = (int)(m.CurrentMonthSpend * 100.0m / m.BudgetLimit);
                        details = $"${m.BudgetLimit - m.CurrentMonthSpend:F2} remaining in budget";
                    }
                    break;
            }

            if (percentUsed >= _config.QuotaWarningThresholdPercent)
            {
                await NotifyQuotaWarningAsync(m.ProviderId, quota.DisplayName, percentUsed, details);
            }
        }
    }

    private bool ShouldSend(NotificationType type)
    {
        // General always sends, others check config
        if (type == NotificationType.General) return true;
        return _config.EnabledTypes.Contains(type);
    }

    private void InitializeChannels()
    {
        _channels.Add(new EmailChannel(logger: _logger));
        _channels.Add(new DiscordChannel(logger: _logger));
        _channels.Add(new NtfyChannel(logger: _logger));
    }

    private NotificationConfig LoadConfig()
    {
        if (!File.Exists(_configPath))
            return new NotificationConfig();

        try
        {
            var json = File.ReadAllText(_configPath);
            return JsonSerializer.Deserialize<NotificationConfig>(json, JsonOptions) ?? new NotificationConfig();
        }
        catch
        {
            return new NotificationConfig();
        }
    }

    private void SaveConfig()
    {
        var dir = Path.GetDirectoryName(_configPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(_config, JsonOptions);
        File.WriteAllText(_configPath, json);
    }

    private static string GetDefaultConfigPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "dotnet-automation", "notifications.json");
    }
}

public class NotificationConfig
{
    public List<string> EnabledChannels { get; set; } = [];

    public List<NotificationType> EnabledTypes { get; set; } =
    [
        NotificationType.TaskCompleted,
        NotificationType.TaskFailed,
        NotificationType.QuotaWarning,
        NotificationType.QuotaExhausted,
        NotificationType.BuildSuccess,
        NotificationType.BuildFailed
    ];

    public int QuotaWarningThresholdPercent { get; set; } = 80;
}

public class NotificationResult
{
    public Notification Notification { get; set; } = null!;
    public bool Success { get; set; }
    public Dictionary<string, bool> ChannelResults { get; set; } = [];
}
