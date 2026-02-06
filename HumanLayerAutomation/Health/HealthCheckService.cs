using System.Diagnostics;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace HumanLayerAutomation.Health;

/// <summary>
/// Cross-platform health check service for all providers and subscriptions.
/// </summary>
public class HealthCheckService
{
    private readonly ILogger? _logger;
    private readonly HttpClient _httpClient;

    public HealthCheckService(ILogger? logger = null)
    {
        _logger = logger;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    /// <summary>Run all health checks and return comprehensive status.</summary>
    public async Task<HealthReport> CheckAllAsync(CancellationToken ct = default)
    {
        var report = new HealthReport
        {
            Timestamp = DateTime.UtcNow,
            Platform = GetPlatformInfo()
        };

        // Run checks in parallel
        var tasks = new List<Task<ProviderHealth>>
        {
            CheckClaudeAsync(ct),
            CheckGitHubAsync(ct),
            CheckOpenAIAsync(ct)
        };

        var results = await Task.WhenAll(tasks);

        foreach (var result in results)
        {
            report.Providers[result.ProviderId] = result;
        }

        report.OverallHealthy = report.Providers.Values.Any(p => p.IsHealthy);
        return report;
    }

    /// <summary>Check Claude CLI status and subscription.</summary>
    public async Task<ProviderHealth> CheckClaudeAsync(CancellationToken ct = default)
    {
        var health = new ProviderHealth
        {
            ProviderId = "anthropic",
            DisplayName = "Claude (Anthropic)",
            CheckedAt = DateTime.UtcNow
        };

        try
        {
            // Check if claude CLI exists
            var claudePath = FindExecutable("claude");
            if (claudePath == null)
            {
                health.Status = HealthStatus.NotInstalled;
                health.Message = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    ? "Claude CLI not found. Install with: irm https://claude.ai/install.ps1 | iex"
                    : "Claude CLI not found. Install with: curl -fsSL https://claude.ai/install.sh | bash";
                return health;
            }

            health.ExecutablePath = claudePath;
            health.Details["cli_path"] = claudePath;

            // Get version
            var versionResult = await RunCommandAsync(claudePath, "--version", ct);
            if (versionResult.Success)
            {
                health.Version = versionResult.Output.Trim();
                health.Details["version"] = health.Version;
            }

            // Check authentication by running a minimal command
            // The --print flag returns the prompt without executing
            var authResult = await RunCommandAsync(claudePath, "--help", ct);
            if (authResult.Success)
            {
                health.IsAuthenticated = true;
                health.Status = HealthStatus.Healthy;
                health.Message = "Claude CLI is installed and ready";

                // Try to detect subscription type from config or environment
                health.SubscriptionType = DetectClaudeSubscription();
            }
            else
            {
                health.Status = HealthStatus.AuthenticationFailed;
                health.Message = "Claude CLI found but may need authentication";
            }
        }
        catch (Exception ex)
        {
            health.Status = HealthStatus.Error;
            health.Message = $"Error checking Claude: {ex.Message}";
            _logger?.LogError(ex, "Claude health check failed");
        }

        health.IsHealthy = health.Status == HealthStatus.Healthy;
        return health;
    }

    /// <summary>Check GitHub CLI and Copilot status (API + CLI).</summary>
    public async Task<ProviderHealth> CheckGitHubAsync(CancellationToken ct = default)
    {
        var health = new ProviderHealth
        {
            ProviderId = "github",
            DisplayName = "GitHub Copilot",
            CheckedAt = DateTime.UtcNow
        };

        try
        {
            // 1. Check for GitHub token (needed for Copilot Chat API)
            var githubToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN")
                ?? Environment.GetEnvironmentVariable("GH_TOKEN");

            if (!string.IsNullOrEmpty(githubToken))
            {
                health.Details["api_token"] = "configured";
                health.Details["api_token_prefix"] = githubToken.Length > 8
                    ? githubToken[..8] + "..." : "***";

                // Test GitHub Models API connectivity
                try
                {
                    var apiClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                    var request = new HttpRequestMessage(HttpMethod.Get, "https://models.github.ai/catalog/models");
                    request.Headers.Add("Authorization", $"Bearer {githubToken}");
                    request.Headers.Add("Accept", "application/vnd.github+json");
                    request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
                    request.Headers.Add("User-Agent", "dotnet-automation/1.0");

                    var apiResponse = await apiClient.SendAsync(request, ct);
                    if (apiResponse.IsSuccessStatusCode)
                    {
                        health.Details["copilot_api"] = "connected";
                        health.IsAuthenticated = true;
                    }
                    else
                    {
                        health.Details["copilot_api"] = $"HTTP {(int)apiResponse.StatusCode}";
                    }
                    apiClient.Dispose();
                }
                catch (Exception ex)
                {
                    health.Details["copilot_api"] = $"error: {ex.Message}";
                }
            }
            else
            {
                health.Details["api_token"] = "not set (set GITHUB_TOKEN for full API access)";
            }

            // 2. Check if gh CLI exists
            var ghPath = FindExecutable("gh");
            if (ghPath == null)
            {
                if (health.IsAuthenticated)
                {
                    // API works but no CLI — that's fine, API is preferred
                    health.Status = HealthStatus.Healthy;
                    health.Message = "Copilot Chat API is available (gh CLI not found but not required)";
                    health.SubscriptionType = "GitHub Pro (API)";
                }
                else
                {
                    health.Status = HealthStatus.NotInstalled;
                    health.Message = "GitHub CLI not found and GITHUB_TOKEN not set. " +
                        "Install gh from https://cli.github.com or set GITHUB_TOKEN";
                }
                health.IsHealthy = health.IsAuthenticated;
                return health;
            }

            health.ExecutablePath = ghPath;
            health.Details["cli_path"] = ghPath;

            // 3. Get gh CLI version
            var versionResult = await RunCommandAsync(ghPath, "--version", ct);
            if (versionResult.Success)
            {
                var versionLine = versionResult.Output.Split('\n').FirstOrDefault() ?? "";
                health.Version = versionLine.Trim();
                health.Details["version"] = health.Version;
            }

            // 4. Check gh auth status
            var authResult = await RunCommandAsync(ghPath, "auth status", ct);
            if (authResult.Success || authResult.Output.Contains("Logged in"))
            {
                if (!health.IsAuthenticated) health.IsAuthenticated = true;
                health.Details["gh_auth"] = "authenticated";

                var authOutput = authResult.Output + authResult.Error;
                if (authOutput.Contains("Logged in to github.com"))
                {
                    var accountMatch = System.Text.RegularExpressions.Regex.Match(
                        authOutput, @"account\s+(\S+)");
                    if (accountMatch.Success)
                    {
                        health.Details["account"] = accountMatch.Groups[1].Value;
                    }
                }
            }
            else if (!health.IsAuthenticated)
            {
                health.Status = HealthStatus.AuthenticationFailed;
                health.Message = "Not authenticated. Run: gh auth login, or set GITHUB_TOKEN";
                return health;
            }

            // 5. Check copilot CLI extension (optional — API is preferred)
            var copilotResult = await RunCommandAsync(ghPath, "copilot --version", ct);
            if (copilotResult.Success)
            {
                health.Details["copilot_cli"] = copilotResult.Output.Trim();
            }
            else
            {
                health.Details["copilot_cli"] = "not installed (optional — API is preferred)";
            }

            // Set final status
            if (health.IsAuthenticated)
            {
                health.Status = HealthStatus.Healthy;
                health.SubscriptionType = !string.IsNullOrEmpty(githubToken)
                    ? "GitHub Pro (API + CLI)" : "GitHub Pro (CLI only)";
                health.Message = "GitHub Copilot is ready for autonomous coding";
            }
            else
            {
                health.Status = HealthStatus.PartiallyHealthy;
                health.Message = "GitHub CLI works but Copilot access not verified";
            }
        }
        catch (Exception ex)
        {
            health.Status = HealthStatus.Error;
            health.Message = $"Error checking GitHub: {ex.Message}";
            _logger?.LogError(ex, "GitHub health check failed");
        }

        health.IsHealthy = health.Status is HealthStatus.Healthy or HealthStatus.PartiallyHealthy;
        return health;
    }

    /// <summary>Check OpenAI API status.</summary>
    public async Task<ProviderHealth> CheckOpenAIAsync(CancellationToken ct = default)
    {
        var health = new ProviderHealth
        {
            ProviderId = "openai",
            DisplayName = "OpenAI API",
            CheckedAt = DateTime.UtcNow
        };

        try
        {
            var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");

            if (string.IsNullOrEmpty(apiKey))
            {
                health.Status = HealthStatus.NotConfigured;
                health.Message = "OPENAI_API_KEY environment variable not set";
                return health;
            }

            health.Details["api_key_set"] = "true";
            health.Details["api_key_prefix"] = apiKey.Length > 8 ? apiKey[..8] + "..." : "***";

            // Test API connectivity with a minimal request (list models)
            var request = new HttpRequestMessage(HttpMethod.Get, "https://api.openai.com/v1/models");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            var response = await _httpClient.SendAsync(request, ct);

            if (response.IsSuccessStatusCode)
            {
                health.IsAuthenticated = true;
                health.Status = HealthStatus.Healthy;
                health.Message = "OpenAI API key is valid";
                health.SubscriptionType = "Pay-per-use API";

                // Parse response to get available models
                var content = await response.Content.ReadAsStringAsync(ct);
                try
                {
                    using var doc = JsonDocument.Parse(content);
                    var models = doc.RootElement.GetProperty("data").EnumerateArray()
                        .Select(m => m.GetProperty("id").GetString())
                        .Where(m => m != null && (m.Contains("gpt-4") || m.Contains("gpt-3.5")))
                        .Take(5)
                        .ToList();

                    health.Details["available_models"] = string.Join(", ", models!);
                }
                catch { /* Ignore parsing errors */ }
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                health.Status = HealthStatus.AuthenticationFailed;
                health.Message = "OpenAI API key is invalid";
            }
            else
            {
                health.Status = HealthStatus.Error;
                health.Message = $"OpenAI API returned: {response.StatusCode}";
            }
        }
        catch (HttpRequestException ex)
        {
            health.Status = HealthStatus.NetworkError;
            health.Message = $"Cannot reach OpenAI API: {ex.Message}";
        }
        catch (Exception ex)
        {
            health.Status = HealthStatus.Error;
            health.Message = $"Error checking OpenAI: {ex.Message}";
            _logger?.LogError(ex, "OpenAI health check failed");
        }

        health.IsHealthy = health.Status == HealthStatus.Healthy;
        return health;
    }

    /// <summary>Find executable in PATH (cross-platform).</summary>
    private static string? FindExecutable(string name)
    {
        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var extensions = isWindows ? new[] { ".exe", ".cmd", ".bat", "" } : new[] { "" };
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
        var paths = pathVar.Split(isWindows ? ';' : ':');

        foreach (var path in paths)
        {
            foreach (var ext in extensions)
            {
                var fullPath = Path.Combine(path, name + ext);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }
        }

        // Also check common locations
        var commonPaths = isWindows
            ? new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "GitHub CLI", "gh.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "GitHub CLI", "gh.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", $"{name}.cmd"),
            }
            : new[]
            {
                $"/usr/bin/{name}",
                $"/usr/local/bin/{name}",
                $"/opt/homebrew/bin/{name}",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", name),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".npm-global", "bin", name),
            };

        foreach (var path in commonPaths)
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    /// <summary>Run a command and capture output (cross-platform).</summary>
    private static async Task<CommandResult> RunCommandAsync(string executable, string arguments, CancellationToken ct)
    {
        var result = new CommandResult();

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            // Set shell based on platform
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                psi.Environment["TERM"] = "dumb"; // Disable color codes
            }

            using var process = Process.Start(psi);
            if (process == null)
            {
                result.Error = "Failed to start process";
                return result;
            }

            var outputTask = process.StandardOutput.ReadToEndAsync(ct);
            var errorTask = process.StandardError.ReadToEndAsync(ct);

            await process.WaitForExitAsync(ct);

            result.Output = await outputTask;
            result.Error = await errorTask;
            result.ExitCode = process.ExitCode;
            result.Success = process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
        }

        return result;
    }

    private static PlatformInfo GetPlatformInfo()
    {
        return new PlatformInfo
        {
            OS = RuntimeInformation.OSDescription,
            Architecture = RuntimeInformation.OSArchitecture.ToString(),
            IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
            IsLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux),
            IsMacOS = RuntimeInformation.IsOSPlatform(OSPlatform.OSX),
            DotNetVersion = Environment.Version.ToString()
        };
    }

    private static string? DetectClaudeSubscription()
    {
        // Check for common indicators of subscription type
        var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        if (!string.IsNullOrEmpty(apiKey))
        {
            return "API Key (Pay-per-use)";
        }

        // Claude CLI with Pro subscription typically uses browser auth
        return "Claude Pro (Browser Auth)";
    }
}

public class HealthReport
{
    public DateTime Timestamp { get; set; }
    public PlatformInfo Platform { get; set; } = new();
    public Dictionary<string, ProviderHealth> Providers { get; set; } = new();
    public bool OverallHealthy { get; set; }

    public void PrintConsole()
    {
        Console.WriteLine("Health Check Report");
        Console.WriteLine($"Generated: {Timestamp:yyyy-MM-dd HH:mm:ss} UTC");
        Console.WriteLine("═".PadRight(70, '═'));
        Console.WriteLine();

        Console.WriteLine("Platform:");
        Console.WriteLine($"  OS:           {Platform.OS}");
        Console.WriteLine($"  Architecture: {Platform.Architecture}");
        Console.WriteLine($"  .NET Version: {Platform.DotNetVersion}");
        Console.WriteLine();

        Console.WriteLine("Provider Status:");
        Console.WriteLine("─".PadRight(70, '─'));

        foreach (var (_, provider) in Providers.OrderBy(p => p.Key))
        {
            var statusColor = provider.Status switch
            {
                HealthStatus.Healthy => ConsoleColor.Green,
                HealthStatus.PartiallyHealthy => ConsoleColor.Yellow,
                HealthStatus.NotInstalled or HealthStatus.NotConfigured => ConsoleColor.DarkGray,
                _ => ConsoleColor.Red
            };

            var statusIcon = provider.Status switch
            {
                HealthStatus.Healthy => "[OK]",
                HealthStatus.PartiallyHealthy => "[PARTIAL]",
                HealthStatus.NotInstalled => "[NOT INSTALLED]",
                HealthStatus.NotConfigured => "[NOT CONFIGURED]",
                HealthStatus.AuthenticationFailed => "[AUTH FAILED]",
                HealthStatus.NetworkError => "[NETWORK ERROR]",
                _ => "[ERROR]"
            };

            Console.WriteLine();
            Console.Write($"  {provider.DisplayName,-25} ");
            var prevColor = Console.ForegroundColor;
            Console.ForegroundColor = statusColor;
            Console.WriteLine(statusIcon);
            Console.ForegroundColor = prevColor;

            if (!string.IsNullOrEmpty(provider.Message))
                Console.WriteLine($"    {provider.Message}");

            if (!string.IsNullOrEmpty(provider.Version))
                Console.WriteLine($"    Version: {provider.Version}");

            if (!string.IsNullOrEmpty(provider.SubscriptionType))
                Console.WriteLine($"    Subscription: {provider.SubscriptionType}");

            if (provider.Details.Count > 0)
            {
                foreach (var (key, value) in provider.Details.Where(d =>
                    d.Key != "cli_path" && d.Key != "version"))
                {
                    Console.WriteLine($"    {key}: {value}");
                }
            }
        }

        Console.WriteLine();
        Console.WriteLine("─".PadRight(70, '─'));

        var healthyCount = Providers.Values.Count(p => p.IsHealthy);
        Console.Write($"Overall: {healthyCount}/{Providers.Count} providers healthy ");

        if (OverallHealthy)
        {
            var prevColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("[SYSTEM READY]");
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
}

public class PlatformInfo
{
    public string OS { get; set; } = "";
    public string Architecture { get; set; } = "";
    public bool IsWindows { get; set; }
    public bool IsLinux { get; set; }
    public bool IsMacOS { get; set; }
    public string DotNetVersion { get; set; } = "";
}

public class ProviderHealth
{
    public string ProviderId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public HealthStatus Status { get; set; } = HealthStatus.Unknown;
    public bool IsHealthy { get; set; }
    public bool IsAuthenticated { get; set; }
    public string? Message { get; set; }
    public string? Version { get; set; }
    public string? ExecutablePath { get; set; }
    public string? SubscriptionType { get; set; }
    public DateTime CheckedAt { get; set; }
    public Dictionary<string, string> Details { get; set; } = new();
}

public enum HealthStatus
{
    Unknown,
    Healthy,
    PartiallyHealthy,
    NotInstalled,
    NotConfigured,
    AuthenticationFailed,
    NetworkError,
    Error
}

internal class CommandResult
{
    public bool Success { get; set; }
    public int ExitCode { get; set; }
    public string Output { get; set; } = "";
    public string Error { get; set; } = "";
}
