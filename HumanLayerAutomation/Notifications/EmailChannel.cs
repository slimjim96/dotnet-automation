using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Logging;

namespace HumanLayerAutomation.Notifications;

/// <summary>
/// Email notification channel using SMTP.
/// Works with Gmail, Outlook, or any SMTP server.
/// </summary>
public class EmailChannel : INotificationChannel
{
    private readonly EmailChannelConfig _config;
    private readonly ILogger? _logger;

    public string ChannelId => "email";
    public string DisplayName => "Email (SMTP)";
    public bool IsConfigured => !string.IsNullOrEmpty(_config.SmtpServer) &&
                                !string.IsNullOrEmpty(_config.FromEmail) &&
                                !string.IsNullOrEmpty(_config.ToEmail);

    public EmailChannel(EmailChannelConfig? config = null, ILogger? logger = null)
    {
        _config = config ?? EmailChannelConfig.FromEnvironment();
        _logger = logger;
    }

    public async Task<bool> SendAsync(Notification notification, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            _logger?.LogWarning("Email channel not configured");
            return false;
        }

        try
        {
            using var client = new SmtpClient(_config.SmtpServer, _config.SmtpPort)
            {
                EnableSsl = _config.UseSsl,
                Credentials = new NetworkCredential(_config.Username ?? _config.FromEmail, _config.Password)
            };

            var subject = $"[{notification.Level}] {notification.Title}";
            var body = FormatEmailBody(notification);

            var message = new MailMessage(_config.FromEmail, _config.ToEmail, subject, body)
            {
                IsBodyHtml = true
            };

            await client.SendMailAsync(message, ct);
            _logger?.LogInformation("Email sent: {Subject}", subject);
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to send email");
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
            Type = NotificationType.General
        }, ct);
    }

    private string FormatEmailBody(Notification notification)
    {
        var levelColor = notification.Level switch
        {
            NotificationLevel.Success => "#28a745",
            NotificationLevel.Warning => "#ffc107",
            NotificationLevel.Error => "#dc3545",
            _ => "#17a2b8"
        };

        var levelEmoji = notification.Level switch
        {
            NotificationLevel.Success => "✅",
            NotificationLevel.Warning => "⚠️",
            NotificationLevel.Error => "❌",
            _ => "ℹ️"
        };

        var dataRows = notification.Data.Count > 0
            ? string.Join("", notification.Data.Select(kv =>
                $"<tr><td style='padding: 5px; border-bottom: 1px solid #eee;'><strong>{kv.Key}:</strong></td><td style='padding: 5px; border-bottom: 1px solid #eee;'>{kv.Value}</td></tr>"))
            : "";

        return $@"
<!DOCTYPE html>
<html>
<head>
    <style>
        body {{ font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
        .header {{ background: {levelColor}; color: white; padding: 15px; border-radius: 5px 5px 0 0; }}
        .content {{ background: #f8f9fa; padding: 20px; border: 1px solid #dee2e6; border-top: none; border-radius: 0 0 5px 5px; }}
        .footer {{ margin-top: 20px; font-size: 12px; color: #6c757d; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h2 style='margin: 0;'>{levelEmoji} {notification.Title}</h2>
        </div>
        <div class='content'>
            <p>{notification.Message}</p>
            {(notification.Data.Count > 0 ? $"<table style='width: 100%; margin-top: 15px;'>{dataRows}</table>" : "")}
        </div>
        <div class='footer'>
            <p>Sent by .NET Automation at {notification.Timestamp:yyyy-MM-dd HH:mm:ss} UTC</p>
            <p>Type: {notification.Type}</p>
        </div>
    </div>
</body>
</html>";
    }
}

public class EmailChannelConfig
{
    public string SmtpServer { get; set; } = "";
    public int SmtpPort { get; set; } = 587;
    public bool UseSsl { get; set; } = true;
    public string? Username { get; set; }
    public string Password { get; set; } = "";
    public string FromEmail { get; set; } = "";
    public string ToEmail { get; set; } = "";

    public static EmailChannelConfig FromEnvironment() => new()
    {
        SmtpServer = Environment.GetEnvironmentVariable("SMTP_SERVER") ?? "",
        SmtpPort = int.TryParse(Environment.GetEnvironmentVariable("SMTP_PORT"), out var port) ? port : 587,
        UseSsl = Environment.GetEnvironmentVariable("SMTP_SSL")?.ToLower() != "false",
        Username = Environment.GetEnvironmentVariable("SMTP_USERNAME"),
        Password = Environment.GetEnvironmentVariable("SMTP_PASSWORD") ?? "",
        FromEmail = Environment.GetEnvironmentVariable("SMTP_FROM") ?? "",
        ToEmail = Environment.GetEnvironmentVariable("NOTIFY_EMAIL") ?? ""
    };

    /// <summary>Gmail configuration helper.</summary>
    public static EmailChannelConfig ForGmail(string gmailAddress, string appPassword, string? toEmail = null) => new()
    {
        SmtpServer = "smtp.gmail.com",
        SmtpPort = 587,
        UseSsl = true,
        Username = gmailAddress,
        Password = appPassword,
        FromEmail = gmailAddress,
        ToEmail = toEmail ?? gmailAddress
    };

    /// <summary>Outlook configuration helper.</summary>
    public static EmailChannelConfig ForOutlook(string outlookEmail, string password, string? toEmail = null) => new()
    {
        SmtpServer = "smtp-mail.outlook.com",
        SmtpPort = 587,
        UseSsl = true,
        Username = outlookEmail,
        Password = password,
        FromEmail = outlookEmail,
        ToEmail = toEmail ?? outlookEmail
    };
}
