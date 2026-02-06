using System.Diagnostics;
using System.Runtime.InteropServices;

namespace HumanLayerAutomation.Health;

/// <summary>
/// Cross-platform setup wizard for configuring providers and notifications.
/// </summary>
public class SetupWizard
{
    private readonly HealthCheckService _healthService;

    public SetupWizard()
    {
        _healthService = new HealthCheckService();
    }

    public async Task RunAsync()
    {
        Console.WriteLine("Setup Wizard");
        Console.WriteLine("═".PadRight(60, '═'));
        Console.WriteLine();
        Console.WriteLine("This wizard will help you configure the automation system.");
        Console.WriteLine();

        // Detect platform
        var platform = DetectPlatform();
        Console.WriteLine($"Detected Platform: {platform.Name}");
        Console.WriteLine();

        // Check each provider
        await CheckAndSetupClaudeAsync(platform);
        await CheckAndSetupGitHubAsync(platform);
        await CheckAndSetupOpenAIAsync();
        await SetupNotificationsAsync();

        Console.WriteLine();
        Console.WriteLine("═".PadRight(60, '═'));
        Console.WriteLine("Setup complete! Run 'dotnet run -- status' to verify.");
    }

    private async Task CheckAndSetupClaudeAsync(PlatformInfo platform)
    {
        Console.WriteLine("─".PadRight(60, '─'));
        Console.WriteLine("1. Claude CLI");
        Console.WriteLine();

        var health = await _healthService.CheckClaudeAsync();

        if (health.IsHealthy)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"   [OK] Claude CLI is installed: {health.Version}");
            Console.ResetColor();
            return;
        }

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("   Claude CLI is not installed.");
        Console.ResetColor();
        Console.WriteLine();
        Console.WriteLine("   Installation (native binary - no Node.js required):");
        Console.WriteLine();

        switch (platform.Type)
        {
            case "windows":
                Console.WriteLine("   Run in PowerShell:");
                Console.WriteLine("     irm https://claude.ai/install.ps1 | iex");
                Console.WriteLine();
                Console.WriteLine("   Alternative - WinGet:");
                Console.WriteLine("     winget install Anthropic.ClaudeCode");
                Console.WriteLine("     (Note: WinGet installs don't auto-update)");
                break;

            case "linux":
                Console.WriteLine("   Run in terminal:");
                Console.WriteLine("     curl -fsSL https://claude.ai/install.sh | bash");
                Console.WriteLine();
                Console.WriteLine("   This works on most Linux distributions including:");
                Console.WriteLine("     Ubuntu, Debian, Fedora, CentOS, RHEL, Arch, etc.");
                break;

            case "macos":
                Console.WriteLine("   Run in terminal:");
                Console.WriteLine("     curl -fsSL https://claude.ai/install.sh | bash");
                Console.WriteLine();
                Console.WriteLine("   Alternative - Homebrew:");
                Console.WriteLine("     brew install claude-code");
                break;
        }

        Console.WriteLine();
        Console.WriteLine("   After installing:");
        Console.WriteLine("     claude --version     # Verify installation");
        Console.WriteLine("     claude doctor        # Check installation health");
        Console.WriteLine("     claude               # Start Claude Code");
        Console.WriteLine();
    }

    private async Task CheckAndSetupGitHubAsync(PlatformInfo platform)
    {
        Console.WriteLine("─".PadRight(60, '─'));
        Console.WriteLine("2. GitHub CLI + Copilot");
        Console.WriteLine();

        var health = await _healthService.CheckGitHubAsync();

        if (health.Status == HealthStatus.Healthy)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"   [OK] GitHub CLI with Copilot is ready: {health.Version}");
            Console.ResetColor();
            return;
        }

        if (health.Status == HealthStatus.NotInstalled)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("   GitHub CLI is not installed.");
            Console.ResetColor();
            Console.WriteLine();
            Console.WriteLine("   Installation:");
            Console.WriteLine();

            switch (platform.Type)
            {
                case "windows":
                    Console.WriteLine("   Option 1 - Winget:");
                    Console.WriteLine("     winget install GitHub.cli");
                    Console.WriteLine();
                    Console.WriteLine("   Option 2 - Chocolatey:");
                    Console.WriteLine("     choco install gh");
                    Console.WriteLine();
                    Console.WriteLine("   Option 3 - Download:");
                    Console.WriteLine("     https://cli.github.com");
                    break;

                case "linux":
                    Console.WriteLine("   Ubuntu/Debian:");
                    Console.WriteLine("     curl -fsSL https://cli.github.com/packages/githubcli-archive-keyring.gpg | sudo dd of=/usr/share/keyrings/githubcli-archive-keyring.gpg");
                    Console.WriteLine("     echo \"deb [arch=$(dpkg --print-architecture) signed-by=/usr/share/keyrings/githubcli-archive-keyring.gpg] https://cli.github.com/packages stable main\" | sudo tee /etc/apt/sources.list.d/github-cli.list > /dev/null");
                    Console.WriteLine("     sudo apt update && sudo apt install gh");
                    Console.WriteLine();
                    Console.WriteLine("   RHEL/CentOS/Fedora:");
                    Console.WriteLine("     sudo dnf install gh");
                    break;

                case "macos":
                    Console.WriteLine("   Homebrew:");
                    Console.WriteLine("     brew install gh");
                    break;
            }
        }
        else if (health.Status == HealthStatus.AuthenticationFailed)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("   GitHub CLI installed but not authenticated.");
            Console.ResetColor();
            Console.WriteLine();
            Console.WriteLine("   Run: gh auth login");
        }
        else if (health.Status == HealthStatus.PartiallyHealthy)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("   GitHub CLI works but Copilot extension not installed.");
            Console.ResetColor();
            Console.WriteLine();
            Console.WriteLine("   Install Copilot extension:");
            Console.WriteLine("     gh extension install github/gh-copilot");
            Console.WriteLine();
            Console.WriteLine("   Note: Requires active GitHub Copilot subscription");
        }

        Console.WriteLine();
    }

    private async Task CheckAndSetupOpenAIAsync()
    {
        Console.WriteLine("─".PadRight(60, '─'));
        Console.WriteLine("3. OpenAI API");
        Console.WriteLine();

        var health = await _healthService.CheckOpenAIAsync();

        if (health.IsHealthy)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("   [OK] OpenAI API key is configured and valid");
            Console.ResetColor();
            return;
        }

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("   OpenAI API is not configured.");
        Console.ResetColor();
        Console.WriteLine();
        Console.WriteLine("   Setup:");
        Console.WriteLine("   1. Get an API key from: https://platform.openai.com/api-keys");
        Console.WriteLine("   2. Set the environment variable:");
        Console.WriteLine();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Console.WriteLine("      Windows (PowerShell):");
            Console.WriteLine("        $env:OPENAI_API_KEY = \"sk-your-key-here\"");
            Console.WriteLine();
            Console.WriteLine("      Windows (permanent):");
            Console.WriteLine("        [Environment]::SetEnvironmentVariable(\"OPENAI_API_KEY\", \"sk-your-key\", \"User\")");
        }
        else
        {
            Console.WriteLine("      Linux/macOS (bash):");
            Console.WriteLine("        export OPENAI_API_KEY=\"sk-your-key-here\"");
            Console.WriteLine();
            Console.WriteLine("      Add to ~/.bashrc or ~/.zshrc for permanent setup");
        }

        Console.WriteLine();
    }

    private async Task SetupNotificationsAsync()
    {
        Console.WriteLine("─".PadRight(60, '─'));
        Console.WriteLine("4. Notifications (Optional)");
        Console.WriteLine();

        Console.WriteLine("   Choose a notification method:");
        Console.WriteLine();
        Console.WriteLine("   A) ntfy.sh (Recommended - Free, no account needed)");
        Console.WriteLine("      1. Install ntfy app on your phone (iOS/Android)");
        Console.WriteLine("      2. Subscribe to a unique topic (e.g., 'my-automation-123')");
        Console.WriteLine("      3. Set: NTFY_TOPIC=my-automation-123");
        Console.WriteLine("      4. Run: dotnet run -- notify enable ntfy");
        Console.WriteLine();
        Console.WriteLine("   B) Discord Webhook (Free with Discord)");
        Console.WriteLine("      1. In Discord: Server Settings > Integrations > Webhooks");
        Console.WriteLine("      2. Create webhook and copy URL");
        Console.WriteLine("      3. Set: DISCORD_WEBHOOK_URL=https://discord.com/api/webhooks/...");
        Console.WriteLine("      4. Run: dotnet run -- notify enable discord");
        Console.WriteLine();
        Console.WriteLine("   C) Email (SMTP)");
        Console.WriteLine("      Set: SMTP_SERVER, SMTP_FROM, SMTP_PASSWORD, NOTIFY_EMAIL");
        Console.WriteLine("      Run: dotnet run -- notify enable email");
        Console.WriteLine();

        await Task.CompletedTask;
    }

    private static PlatformInfo DetectPlatform()
    {
        var info = new PlatformInfo();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            info.Type = "windows";
            info.Name = "Windows";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            info.Type = "linux";
            info.Name = "Linux";

            // Try to detect distro
            if (File.Exists("/etc/os-release"))
            {
                try
                {
                    var lines = File.ReadAllLines("/etc/os-release");
                    var id = lines.FirstOrDefault(l => l.StartsWith("ID="))?.Split('=')[1].Trim('"');
                    var prettyName = lines.FirstOrDefault(l => l.StartsWith("PRETTY_NAME="))?.Split('=')[1].Trim('"');

                    info.Name = prettyName ?? info.Name;
                    info.Distro = id;
                }
                catch { }
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            info.Type = "macos";
            info.Name = "macOS";
        }

        return info;
    }

    private class PlatformInfo
    {
        public string Type { get; set; } = "unknown";
        public string Name { get; set; } = "Unknown";
        public string? Distro { get; set; }
    }
}
