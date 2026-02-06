using System.Text;
using Microsoft.Extensions.Logging;

namespace HumanLayerAutomation.Notifications;

/// <summary>
/// ntfy.sh notification channel - free push notifications to your phone.
///
/// Setup:
/// 1. Install ntfy app on your phone (iOS/Android)
/// 2. Subscribe to a topic (e.g., "my-automation-alerts")
/// 3. Set NTFY_TOPIC environment variable
///
/// No account required! Just pick a unique topic name.
/// https://ntfy.sh
/// </summary>
public class NtfyChannel : INotificationChannel
{
    private readonly HttpClient _httpClient;
    private readonly NtfyConfig _config;
    private readonly ILogger? _logger;

    public string ChannelId => "ntfy";
    public string DisplayName => "ntfy.sh (Phone Push)";
    public bool IsConfigured => !string.IsNullOrEmpty(_config.Topic);

    public NtfyChannel(NtfyConfig? config = null, ILogger? logger = null)
    {
        _config = config ?? NtfyConfig.FromEnvironment();
        _httpClient = new HttpClient();
        _logger = logger;
    }

    public async Task<bool> SendAsync(Notification notification, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            _logger?.LogWarning("ntfy topic not configured");
            return false;
        }

        try
        {
            var url = $"{_config.ServerUrl}/{_config.Topic}";

            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(notification.Message, Encoding.UTF8, "text/plain")
            };

            // Add headers for rich notifications
            request.Headers.Add("Title", notification.Title);
            request.Headers.Add("Priority", GetPriority(notification.Level));
            request.Headers.Add("Tags", GetTags(notification));

            // Add click action if applicable
            if (notification.Data.TryGetValue("Url", out var clickUrl))
            {
                request.Headers.Add("Click", clickUrl);
            }

            // Add authentication if configured
            if (!string.IsNullOrEmpty(_config.AccessToken))
            {
                request.Headers.Add("Authorization", $"Bearer {_config.AccessToken}");
            }

            var response = await _httpClient.SendAsync(request, ct);

            if (response.IsSuccessStatusCode)
            {
                _logger?.LogInformation("ntfy notification sent: {Title}", notification.Title);
                return true;
            }

            var error = await response.Content.ReadAsStringAsync(ct);
            _logger?.LogError("ntfy failed: {Status} - {Error}", response.StatusCode, error);
            return false;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to send ntfy notification");
            return false;
        }
    }

    public async Task<bool> TestAsync(CancellationToken ct = default)
    {
        return await SendAsync(new Notification
        {
            Title = "Test Notification 🔔",
            Message = "This is a test notification from .NET Automation. If you see this, notifications are working!",
            Level = NotificationLevel.Info,
            Type = NotificationType.General
        }, ct);
    }

    private static string GetPriority(NotificationLevel level) => level switch
    {
        NotificationLevel.Error => "urgent",
        NotificationLevel.Warning => "high",
        NotificationLevel.Success => "default",
        _ => "default"
    };

    private static string GetTags(Notification notification)
    {
        var tags = new List<string>();

        // Add emoji tag based on level
        tags.Add(notification.Level switch
        {
            NotificationLevel.Success => "white_check_mark",
            NotificationLevel.Warning => "warning",
            NotificationLevel.Error => "x",
            _ => "information_source"
        });

        // Add type-specific tags
        tags.Add(notification.Type switch
        {
            NotificationType.TaskCompleted => "checkered_flag",
            NotificationType.TaskFailed => "skull",
            NotificationType.QuotaWarning => "hourglass",
            NotificationType.QuotaExhausted => "stop_sign",
            NotificationType.BuildSuccess => "hammer",
            NotificationType.BuildFailed => "construction",
            _ => "robot"
        });

        return string.Join(",", tags);
    }
}

public class NtfyConfig
{
    /// <summary>ntfy server URL (default: https://ntfy.sh)</summary>
    public string ServerUrl { get; set; } = "https://ntfy.sh";

    /// <summary>Topic name (like a channel). Pick something unique!</summary>
    public string Topic { get; set; } = "";

    /// <summary>Optional access token for private topics.</summary>
    public string? AccessToken { get; set; }

    public static NtfyConfig FromEnvironment() => new()
    {
        ServerUrl = Environment.GetEnvironmentVariable("NTFY_SERVER") ?? "https://ntfy.sh",
        Topic = Environment.GetEnvironmentVariable("NTFY_TOPIC") ?? "",
        AccessToken = Environment.GetEnvironmentVariable("NTFY_TOKEN")
    };
}
