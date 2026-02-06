using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace HumanLayerAutomation.Notifications;

/// <summary>
/// Discord webhook notification channel.
/// Free, supports phone notifications via Discord app.
///
/// Setup:
/// 1. In Discord, go to Server Settings > Integrations > Webhooks
/// 2. Create a new webhook and copy the URL
/// 3. Set DISCORD_WEBHOOK_URL environment variable
/// </summary>
public class DiscordChannel : INotificationChannel
{
    private readonly HttpClient _httpClient;
    private readonly string? _webhookUrl;
    private readonly ILogger? _logger;

    public string ChannelId => "discord";
    public string DisplayName => "Discord Webhook";
    public bool IsConfigured => !string.IsNullOrEmpty(_webhookUrl);

    public DiscordChannel(string? webhookUrl = null, ILogger? logger = null)
    {
        _webhookUrl = webhookUrl ?? Environment.GetEnvironmentVariable("DISCORD_WEBHOOK_URL");
        _httpClient = new HttpClient();
        _logger = logger;
    }

    public async Task<bool> SendAsync(Notification notification, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            _logger?.LogWarning("Discord webhook not configured");
            return false;
        }

        try
        {
            var embed = new DiscordEmbed
            {
                Title = $"{GetEmoji(notification.Level)} {notification.Title}",
                Description = notification.Message,
                Color = GetColor(notification.Level),
                Timestamp = notification.Timestamp.ToString("o"),
                Footer = new DiscordFooter
                {
                    Text = $".NET Automation | {notification.Type}"
                },
                Fields = notification.Data.Select(kv => new DiscordField
                {
                    Name = kv.Key,
                    Value = kv.Value,
                    Inline = true
                }).ToList()
            };

            var payload = new DiscordWebhookPayload
            {
                Username = "Automation Bot",
                Embeds = [embed]
            };

            var response = await _httpClient.PostAsJsonAsync(_webhookUrl, payload, ct);

            if (response.IsSuccessStatusCode)
            {
                _logger?.LogInformation("Discord notification sent: {Title}", notification.Title);
                return true;
            }

            var error = await response.Content.ReadAsStringAsync(ct);
            _logger?.LogError("Discord webhook failed: {Status} - {Error}", response.StatusCode, error);
            return false;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to send Discord notification");
            return false;
        }
    }

    public async Task<bool> TestAsync(CancellationToken ct = default)
    {
        return await SendAsync(new Notification
        {
            Title = "Test Notification",
            Message = "This is a test notification from .NET Automation.",
            Level = NotificationLevel.Info,
            Type = NotificationType.General,
            Data = new Dictionary<string, string>
            {
                ["Source"] = "Test",
                ["Time"] = DateTime.Now.ToString("HH:mm:ss")
            }
        }, ct);
    }

    private static string GetEmoji(NotificationLevel level) => level switch
    {
        NotificationLevel.Success => "✅",
        NotificationLevel.Warning => "⚠️",
        NotificationLevel.Error => "❌",
        _ => "ℹ️"
    };

    private static int GetColor(NotificationLevel level) => level switch
    {
        NotificationLevel.Success => 0x28a745, // Green
        NotificationLevel.Warning => 0xffc107, // Yellow
        NotificationLevel.Error => 0xdc3545,   // Red
        _ => 0x17a2b8                           // Blue
    };
}

// Discord webhook payload models
internal class DiscordWebhookPayload
{
    [JsonPropertyName("username")]
    public string? Username { get; set; }

    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("embeds")]
    public List<DiscordEmbed>? Embeds { get; set; }
}

internal class DiscordEmbed
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("color")]
    public int Color { get; set; }

    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; set; }

    [JsonPropertyName("footer")]
    public DiscordFooter? Footer { get; set; }

    [JsonPropertyName("fields")]
    public List<DiscordField>? Fields { get; set; }
}

internal class DiscordFooter
{
    [JsonPropertyName("text")]
    public string? Text { get; set; }
}

internal class DiscordField
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("value")]
    public string? Value { get; set; }

    [JsonPropertyName("inline")]
    public bool Inline { get; set; }
}
