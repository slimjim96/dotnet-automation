using System.Diagnostics;
using System.Text;
using HumanLayerAutomation.Models;
using Microsoft.Extensions.Logging;

namespace HumanLayerAutomation.Providers;

/// <summary>
/// GitHub Copilot CLI provider using `gh copilot` commands.
/// Requires GitHub CLI with Copilot extension installed.
/// </summary>
public class GitHubCopilotProvider : ICodeProvider
{
    private readonly ModelRegistry _modelRegistry;
    private readonly QuotaManager _quotaManager;
    private readonly ILogger? _logger;
    private readonly GitHubCopilotOptions _options;
    private bool? _isAvailable;

    public string ProviderId => "github";

    public GitHubCopilotProvider(
        ModelRegistry modelRegistry,
        QuotaManager? quotaManager = null,
        GitHubCopilotOptions? options = null,
        ILogger? logger = null)
    {
        _modelRegistry = modelRegistry;
        _quotaManager = quotaManager ?? new QuotaManager();
        _options = options ?? new GitHubCopilotOptions();
        _logger = logger;
    }

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        if (_isAvailable.HasValue)
            return _isAvailable.Value;

        try
        {
            // Check if gh CLI is available
            var ghResult = await RunCommandAsync("gh", "--version", ct: ct);
            if (!ghResult.Success)
            {
                _isAvailable = false;
                return false;
            }

            // Check if copilot extension is installed
            var copilotResult = await RunCommandAsync("gh", "copilot --help", ct: ct);
            _isAvailable = copilotResult.Success;

            if (!_isAvailable.Value)
            {
                _logger?.LogWarning("GitHub Copilot CLI not available. Install with: gh extension install github/gh-copilot");
            }

            return _isAvailable.Value;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Error checking GitHub Copilot availability");
            _isAvailable = false;
            return false;
        }
    }

    public async Task<ProviderResult> RunAsync(ProviderRequest request, CancellationToken ct = default)
    {
        var startTime = DateTime.UtcNow;
        var result = new ProviderResult
        {
            ProviderId = ProviderId,
            ModelId = "github-copilot"
        };

        try
        {
            // Check quota first (monthly premium prompts)
            var quotaCheck = _quotaManager.CheckQuota(ProviderId);
            if (!quotaCheck.IsAvailable)
            {
                result.Success = false;
                result.Error = quotaCheck.Reason;
                result.QuotaExhausted = true;
                return result;
            }

            // Warn about pacing
            if (!string.IsNullOrEmpty(quotaCheck.PacingWarning))
            {
                _logger?.LogWarning("GitHub Copilot pacing warning: {Warning}", quotaCheck.PacingWarning);
            }

            if (!await IsAvailableAsync(ct))
            {
                result.Success = false;
                result.Error = "GitHub Copilot CLI is not available";
                return result;
            }

            // Determine which copilot command to use
            var command = DetermineCommand(request);
            var args = BuildArguments(command, request);

            _logger?.LogInformation("Running gh copilot {Command}", command);

            var cmdResult = await RunCommandAsync("gh", args, request.WorkingDirectory, ct);

            result.Success = cmdResult.Success;
            result.Output = cmdResult.Output;
            result.Error = cmdResult.Error;
            result.Duration = DateTime.UtcNow - startTime;

            // GitHub Copilot is subscription-based, no per-token cost
            result.Cost = 0;

            // Estimate tokens for tracking
            result.InputTokens = EstimateTokens(request.Prompt);
            result.OutputTokens = EstimateTokens(result.Output ?? "");

            // Record usage in both registries (1 premium prompt per request)
            _modelRegistry.RecordUsage("github-copilot", result.InputTokens, result.OutputTokens, 0);
            _quotaManager.RecordUsage(ProviderId, result.InputTokens, result.OutputTokens, premiumPrompts: 1);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
            result.Duration = DateTime.UtcNow - startTime;
            _logger?.LogError(ex, "GitHub Copilot provider error");
        }

        return result;
    }

    public Task<QuotaInfo?> GetQuotaAsync(CancellationToken ct = default)
    {
        var metrics = _quotaManager.GetMetrics(ProviderId);

        return Task.FromResult<QuotaInfo?>(new QuotaInfo
        {
            IsUnlimited = false,
            RemainingRequests = metrics.MonthlyLimit - metrics.CurrentMonthUsage
        });
    }

    private string DetermineCommand(ProviderRequest request)
    {
        var prompt = request.Prompt.ToLower();

        // Use 'explain' for understanding code
        if (prompt.Contains("explain") || prompt.Contains("what does") || prompt.Contains("how does"))
        {
            return "explain";
        }

        // Use 'suggest' for generating code/commands
        return "suggest";
    }

    private string BuildArguments(string command, ProviderRequest request)
    {
        var sb = new StringBuilder();
        sb.Append($"copilot {command}");

        // Add the prompt
        var escapedPrompt = request.Prompt.Replace("\"", "\\\"");
        sb.Append($" \"{escapedPrompt}\"");

        // Add target type for suggest command
        if (command == "suggest")
        {
            sb.Append(" --target shell"); // or 'gh' for gh CLI commands
        }

        return sb.ToString();
    }

    private async Task<(bool Success, string Output, string? Error)> RunCommandAsync(
        string command,
        string args,
        string? workingDir = null,
        CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = command,
            Arguments = args,
            WorkingDirectory = workingDir ?? Environment.CurrentDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        try
        {
            using var process = Process.Start(psi);
            if (process == null)
            {
                return (false, "", "Failed to start process");
            }

            var output = await process.StandardOutput.ReadToEndAsync(ct);
            var error = await process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);

            return (process.ExitCode == 0, output, string.IsNullOrEmpty(error) ? null : error);
        }
        catch (Exception ex)
        {
            return (false, "", ex.Message);
        }
    }

    private static int EstimateTokens(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        // Rough estimate: ~4 characters per token
        return text.Length / 4;
    }
}

public class GitHubCopilotOptions
{
    /// <summary>Preferred command type: suggest, explain</summary>
    public string DefaultCommand { get; set; } = "suggest";

    /// <summary>Target for suggest: shell, gh</summary>
    public string SuggestTarget { get; set; } = "shell";
}
