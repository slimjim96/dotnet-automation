using System.Runtime.InteropServices;
using HumanLayerAutomation.Models;
using HumanLayerAutomation.Notifications;
using Microsoft.Extensions.Logging;

namespace HumanLayerAutomation.Health;

/// <summary>
/// Consolidated system status view combining health, quotas, and notifications.
/// </summary>
public class SystemStatus
{
    private readonly HealthCheckService _healthService;
    private readonly QuotaManager _quotaManager;
    private readonly NotificationManager _notificationManager;
    private readonly ILogger? _logger;

    public SystemStatus(ILogger? logger = null)
    {
        _logger = logger;
        _healthService = new HealthCheckService(logger);
        _quotaManager = new QuotaManager();
        _notificationManager = new NotificationManager(logger);
    }

    /// <summary>Generate a full system status report.</summary>
    public async Task<SystemStatusReport> GetFullStatusAsync(CancellationToken ct = default)
    {
        var report = new SystemStatusReport
        {
            GeneratedAt = DateTime.UtcNow,
            Platform = GetPlatformDetails()
        };

        // Health checks
        report.Health = await _healthService.CheckAllAsync(ct);

        // Quota status
        foreach (var metrics in _quotaManager.GetAllMetrics())
        {
            var quota = _quotaManager.GetProviderQuota(metrics.ProviderId);
            if (quota != null)
            {
                report.Quotas[metrics.ProviderId] = new QuotaStatus
                {
                    ProviderId = metrics.ProviderId,
                    DisplayName = quota.DisplayName,
                    BillingModel = quota.BillingModel,
                    IsEnabled = quota.Enabled,
                    Metrics = metrics,
                    TimeUntilReset = quota.BillingModel == BillingModel.TimeBasedReset
                        ? _quotaManager.GetTimeUntilReset(metrics.ProviderId)
                        : null
                };
            }
        }

        // Notification status
        foreach (var channel in _notificationManager.Channels)
        {
            report.Notifications[channel.ChannelId] = new NotificationStatus
            {
                ChannelId = channel.ChannelId,
                DisplayName = channel.DisplayName,
                IsConfigured = channel.IsConfigured,
                IsEnabled = _notificationManager.EnabledChannels.Contains(channel.ChannelId)
            };
        }

        return report;
    }

    /// <summary>Print a compact status summary to console.</summary>
    public async Task PrintCompactStatusAsync(CancellationToken ct = default)
    {
        var report = await GetFullStatusAsync(ct);

        Console.WriteLine("System Status");
        Console.WriteLine($"Generated: {report.GeneratedAt:yyyy-MM-dd HH:mm:ss} UTC");
        Console.WriteLine("═".PadRight(70, '═'));

        // Platform
        Console.WriteLine();
        Console.WriteLine($"Platform: {report.Platform.OSName} ({report.Platform.Architecture}) | .NET {report.Platform.DotNetVersion}");

        // Providers - compact view
        Console.WriteLine();
        Console.WriteLine("Providers:");
        foreach (var (id, health) in report.Health.Providers)
        {
            var statusIcon = health.IsHealthy ? "+" : "-";
            var statusColor = health.IsHealthy ? ConsoleColor.Green : ConsoleColor.Red;

            // Get quota info if available
            var quotaInfo = "";
            if (report.Quotas.TryGetValue(id, out var quota) && quota.IsEnabled)
            {
                quotaInfo = quota.BillingModel switch
                {
                    BillingModel.TimeBasedReset => $" | Resets in {quota.TimeUntilReset:hh\\:mm}",
                    BillingModel.MonthlyLimit => $" | {quota.Metrics.CurrentMonthUsage}/{quota.Metrics.MonthlyLimit} used",
                    BillingModel.PayPerUse => $" | ${quota.Metrics.CurrentMonthSpend:F2} spent",
                    _ => ""
                };
            }

            Console.Write($"  [{statusIcon}] {health.DisplayName,-25}");
            var prevColor = Console.ForegroundColor;
            Console.ForegroundColor = statusColor;
            Console.Write($" {health.Status,-15}");
            Console.ForegroundColor = prevColor;
            Console.WriteLine(quotaInfo);
        }

        // Notifications - compact view
        Console.WriteLine();
        Console.WriteLine("Notifications:");
        var enabledChannels = report.Notifications.Values.Where(n => n.IsEnabled && n.IsConfigured).ToList();
        if (enabledChannels.Count > 0)
        {
            Console.WriteLine($"  Active: {string.Join(", ", enabledChannels.Select(c => c.DisplayName))}");
        }
        else
        {
            Console.WriteLine("  No channels enabled");
        }

        // Overall status
        Console.WriteLine();
        Console.WriteLine("─".PadRight(70, '─'));
        var healthyProviders = report.Health.Providers.Values.Count(p => p.IsHealthy);
        var totalProviders = report.Health.Providers.Count;

        Console.Write($"Ready: {healthyProviders}/{totalProviders} providers ");
        if (healthyProviders > 0)
        {
            var prevColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("[OPERATIONAL]");
            Console.ForegroundColor = prevColor;
        }
        else
        {
            var prevColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[NO PROVIDERS AVAILABLE]");
            Console.ForegroundColor = prevColor;
        }
    }

    private static PlatformDetails GetPlatformDetails()
    {
        var details = new PlatformDetails
        {
            Architecture = RuntimeInformation.OSArchitecture.ToString(),
            DotNetVersion = Environment.Version.ToString(),
            ProcessorCount = Environment.ProcessorCount,
            Is64Bit = Environment.Is64BitOperatingSystem
        };

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            details.OSName = "Windows";
            details.OSType = "windows";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            details.OSName = "Linux";
            details.OSType = "linux";

            // Try to get distro info
            if (File.Exists("/etc/os-release"))
            {
                try
                {
                    var lines = File.ReadAllLines("/etc/os-release");
                    var prettyName = lines.FirstOrDefault(l => l.StartsWith("PRETTY_NAME="));
                    if (prettyName != null)
                    {
                        details.OSName = prettyName.Split('=')[1].Trim('"');
                    }
                }
                catch { /* Ignore */ }
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            details.OSName = "macOS";
            details.OSType = "macos";
        }
        else
        {
            details.OSName = RuntimeInformation.OSDescription;
            details.OSType = "unknown";
        }

        return details;
    }
}

public class SystemStatusReport
{
    public DateTime GeneratedAt { get; set; }
    public PlatformDetails Platform { get; set; } = new();
    public HealthReport Health { get; set; } = new();
    public Dictionary<string, QuotaStatus> Quotas { get; set; } = new();
    public Dictionary<string, NotificationStatus> Notifications { get; set; } = new();
}

public class PlatformDetails
{
    public string OSName { get; set; } = "";
    public string OSType { get; set; } = "";
    public string Architecture { get; set; } = "";
    public string DotNetVersion { get; set; } = "";
    public int ProcessorCount { get; set; }
    public bool Is64Bit { get; set; }
}

public class QuotaStatus
{
    public string ProviderId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public BillingModel BillingModel { get; set; }
    public bool IsEnabled { get; set; }
    public ProviderMetrics Metrics { get; set; } = new();
    public TimeSpan? TimeUntilReset { get; set; }
}

public class NotificationStatus
{
    public string ChannelId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public bool IsConfigured { get; set; }
    public bool IsEnabled { get; set; }
}
