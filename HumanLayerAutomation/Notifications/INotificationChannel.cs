namespace HumanLayerAutomation.Notifications;

/// <summary>
/// Interface for notification channels.
/// </summary>
public interface INotificationChannel
{
    string ChannelId { get; }
    string DisplayName { get; }
    bool IsConfigured { get; }

    Task<bool> SendAsync(Notification notification, CancellationToken ct = default);
    Task<bool> TestAsync(CancellationToken ct = default);
}

/// <summary>
/// Notification message to send.
/// </summary>
public class Notification
{
    public required string Title { get; set; }
    public required string Message { get; set; }
    public NotificationLevel Level { get; set; } = NotificationLevel.Info;
    public NotificationType Type { get; set; } = NotificationType.General;
    public Dictionary<string, string> Data { get; set; } = [];
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public enum NotificationLevel
{
    Info,
    Warning,
    Error,
    Success
}

public enum NotificationType
{
    General,
    TaskStarted,
    TaskCompleted,
    TaskFailed,
    QuotaWarning,
    QuotaExhausted,
    QuotaReset,
    BuildSuccess,
    BuildFailed
}
