using System.Diagnostics;
using System.Text;
using HumanLayerAutomation.Models;
using Microsoft.Extensions.Logging;

namespace HumanLayerAutomation.Providers;

/// <summary>
/// GitHub Copilot provider with dual-mode operation:
/// 
/// PRIMARY: Copilot Chat Completions API (api.githubcopilot.com)
///   - Full coding capability: generates code, fixes errors, explains, refactors
///   - Uses GitHub token (GITHUB_TOKEN / GH_TOKEN) for auth
///   - Supports system prompts, temperature, max tokens
///   - Compatible with the same prompt patterns as Claude and OpenAI providers
///
/// FALLBACK: `gh copilot suggest/explain` CLI
///   - Only for shell command suggestions when API is unavailable
///   - Limited: interactive TUI commands, cannot generate code files
///
/// With GitHub Pro, you get 1000 premium prompts/month via the API.
/// All actions are auto-approved for autonomous operation.
/// Build errors and warnings are fed back into the conversation automatically.
/// </summary>
public class GitHubCopilotProvider : ICodeProvider, IDisposable
{
    private readonly GitHubCopilotClient _apiClient;
    private readonly ModelRegistry _modelRegistry;
    private readonly QuotaManager _quotaManager;
    private readonly ILogger? _logger;
    private readonly GitHubCopilotOptions _options;
    private bool? _apiAvailable;
    private bool? _cliAvailable;

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

        _apiClient = new GitHubCopilotClient(
            new GitHubCopilotClientOptions
            {
                BaseUrl = _options.ApiBaseUrl,
                Token = _options.GitHubToken,
                TimeoutMinutes = _options.TimeoutMinutes
            },
            logger);
    }

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        // Check API first (preferred path — full coding capability)
        if (_apiAvailable.HasValue && _apiAvailable.Value)
            return true;

        try
        {
            _apiAvailable = await _apiClient.IsAvailableAsync(ct);
            if (_apiAvailable.Value)
            {
                _logger?.LogInformation("GitHub Copilot API is available");
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Copilot API check failed");
            _apiAvailable = false;
        }

        // Fallback: check gh CLI + copilot extension
        if (!_cliAvailable.HasValue)
        {
            _cliAvailable = await CheckCliAvailableAsync(ct);
        }

        if (_cliAvailable.Value)
        {
            _logger?.LogInformation("GitHub Copilot CLI available (limited to shell suggestions)");
        }

        return _apiAvailable == true || _cliAvailable == true;
    }

    /// <summary>Whether the API path is available (full coding capability).</summary>
    public bool IsApiAvailable => _apiAvailable == true;

    public async Task<ProviderResult> RunAsync(ProviderRequest request, CancellationToken ct = default)
    {
        var startTime = DateTime.UtcNow;
        var result = new ProviderResult
        {
            ProviderId = ProviderId,
            ModelId = _options.DefaultModel
        };

        try
        {
            // Quota check
            var quotaCheck = _quotaManager.CheckQuota(ProviderId);
            if (!quotaCheck.IsAvailable)
            {
                result.Success = false;
                result.Error = quotaCheck.Reason;
                result.QuotaExhausted = true;
                return result;
            }

            if (!string.IsNullOrEmpty(quotaCheck.PacingWarning))
            {
                _logger?.LogWarning("GitHub Copilot pacing: {Warning}", quotaCheck.PacingWarning);
            }

            // Ensure availability is checked
            if (!_apiAvailable.HasValue && !_cliAvailable.HasValue)
            {
                await IsAvailableAsync(ct);
            }

            // Route to API (full coding) or CLI (shell-only fallback)
            if (_apiAvailable == true)
            {
                result = await RunViaApiAsync(request, startTime, ct);
            }
            else if (_cliAvailable == true)
            {
                result = await RunViaCliAsync(request, startTime, ct);
            }
            else
            {
                result.Success = false;
                result.Error = "GitHub Copilot not available. " +
                    "Set GITHUB_TOKEN for API access, or install gh CLI with Copilot extension.";
                result.Duration = DateTime.UtcNow - startTime;
                return result;
            }

            // Record usage for quota tracking (1 premium prompt per request)
            _modelRegistry.RecordUsage(
                result.ModelId ?? _options.DefaultModel,
                result.InputTokens,
                result.OutputTokens,
                result.Cost);
            _quotaManager.RecordUsage(
                ProviderId, result.InputTokens, result.OutputTokens, premiumPrompts: 1);
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

    // ========================================================================
    // API-based execution (primary path — full coding capability)
    // ========================================================================

    private async Task<ProviderResult> RunViaApiAsync(
        ProviderRequest request,
        DateTime startTime,
        CancellationToken ct)
    {
        var result = new ProviderResult { ProviderId = ProviderId };

        // Resolve model
        var modelAlias = request.Model ?? _options.DefaultModel;
        var modelInfo = _modelRegistry.GetModelByAlias(modelAlias);
        var modelId = modelInfo?.Id ?? MapModelAlias(modelAlias);
        result.ModelId = modelId;

        // Build messages — same pattern as Claude and OpenAI providers
        var messages = new List<CopilotMessage>();

        if (!string.IsNullOrEmpty(request.SystemPrompt))
        {
            messages.Add(new CopilotMessage { Role = "system", Content = request.SystemPrompt });
        }
        else
        {
            // Default system prompt for autonomous coding
            messages.Add(new CopilotMessage
            {
                Role = "system",
                Content = GetDefaultSystemPrompt()
            });
        }

        messages.Add(new CopilotMessage { Role = "user", Content = request.Prompt });

        var chatRequest = new CopilotChatRequest
        {
            Model = modelId,
            Messages = messages,
            MaxTokens = request.MaxTokens ?? _options.DefaultMaxTokens,
            Temperature = _options.Temperature
        };

        _logger?.LogInformation("Copilot API: model={Model}, prompt={Length} chars",
            modelId, request.Prompt.Length);

        var response = await _apiClient.ChatWithRetryAsync(chatRequest, _options.MaxRetries, ct);

        if (response.Error != null)
        {
            result.Success = false;
            result.Error = response.Error.Message;
            result.Duration = DateTime.UtcNow - startTime;

            // Detect quota exhaustion for MultiProvider fallback
            if (response.Error.StatusCode == 429 ||
                response.Error.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase) ||
                response.Error.Message.Contains("quota", StringComparison.OrdinalIgnoreCase))
            {
                result.QuotaExhausted = true;
            }

            return result;
        }

        var content = response.Choices?.FirstOrDefault()?.Message?.Content;

        result.Success = !string.IsNullOrEmpty(content);
        result.Output = content ?? "";
        result.InputTokens = response.Usage?.PromptTokens ?? EstimateTokens(request.Prompt);
        result.OutputTokens = response.Usage?.CompletionTokens ?? EstimateTokens(content ?? "");
        result.Duration = DateTime.UtcNow - startTime;

        // Cost is $0 for Copilot Pro (subscription-based), but we track tokens for quota
        result.Cost = 0;

        return result;
    }

    // ========================================================================
    // CLI-based execution (fallback — shell suggestions only)
    // ========================================================================

    private async Task<ProviderResult> RunViaCliAsync(
        ProviderRequest request,
        DateTime startTime,
        CancellationToken ct)
    {
        var result = new ProviderResult
        {
            ProviderId = ProviderId,
            ModelId = "github-copilot-cli"
        };

        // CLI can only do suggest/explain — warn if this is a coding task
        if (IsCodingTask(request.Prompt))
        {
            _logger?.LogWarning(
                "Copilot CLI cannot generate code files. " +
                "Set GITHUB_TOKEN for full API access. Attempting shell suggest as best effort.");
        }

        var command = DetermineCliCommand(request.Prompt);
        var args = BuildCliArguments(command, request.Prompt);

        _logger?.LogInformation("Running gh copilot {Command} (CLI fallback)", command);

        var cmdResult = await RunCommandAsync("gh", args, request.WorkingDirectory, ct);

        result.Success = cmdResult.Success;
        result.Output = cmdResult.Output;
        result.Error = cmdResult.Error;
        result.Duration = DateTime.UtcNow - startTime;
        result.Cost = 0;
        result.InputTokens = EstimateTokens(request.Prompt);
        result.OutputTokens = EstimateTokens(result.Output ?? "");

        return result;
    }

    // ========================================================================
    // Helpers
    // ========================================================================

    /// <summary>
    /// Default system prompt for autonomous coding. Matches the patterns used by
    /// Claude and OpenAI providers so that prompts from AutoBuilder and
    /// AutomatedAppBuilder work consistently across all providers.
    /// </summary>
    private static string GetDefaultSystemPrompt() => """
        You are an expert software engineer acting as an autonomous coding agent.
        
        RULES:
        - Generate complete, working code — never stubs or placeholders
        - When asked to create files, output ONLY the file content with no markdown fences unless explicitly asked
        - When fixing build errors, analyze the error carefully and output the corrected code
        - Follow existing project conventions when modifying code
        - Prefer minimal, focused changes over large refactors
        - All actions are pre-approved — do not ask for confirmation
        - If you encounter ambiguity, choose the most reasonable option and proceed
        
        OUTPUT FORMAT:
        - For code generation: output raw code, no commentary
        - For explanations: be concise, 3-5 bullet points max
        - For error fixes: output the full corrected file content
        """;

    /// <summary>
    /// Map user-friendly aliases to Copilot API model IDs.
    /// GitHub Copilot Pro exposes GPT-4o and Claude Sonnet via the API.
    /// </summary>
    private static string MapModelAlias(string alias) => alias.ToLower() switch
    {
        // Map friendly aliases to publisher/model format for GitHub Models API
        "copilot" or "github-copilot" or "gh-copilot" => "openai/gpt-4o",
        "gpt4o" or "gpt-4o" or "gpt-4-omni" => "openai/gpt-4o",
        "gpt4o-mini" or "gpt-4o-mini" => "openai/gpt-4o-mini",
        "gpt-4.1" or "gpt4.1" => "openai/gpt-4.1",
        "gpt-4.1-mini" => "openai/gpt-4.1-mini",
        "gpt-4.1-nano" => "openai/gpt-4.1-nano",
        "o1" => "openai/o1",
        "o1-mini" => "openai/o1-mini",
        "o3-mini" => "openai/o3-mini",
        "o4-mini" => "openai/o4-mini",
        "claude-sonnet" or "copilot-sonnet" => "anthropic/claude-3.5-sonnet",
        // Already in publisher/model format — pass through
        var m when m.Contains('/') => m,
        _ => "openai/gpt-4o" // Default to GPT-4o for best coding results
    };

    private static bool IsCodingTask(string prompt)
    {
        var lower = prompt.ToLower();
        return lower.Contains("create") || lower.Contains("generate") ||
               lower.Contains("implement") || lower.Contains("write") ||
               lower.Contains("fix") || lower.Contains("build") ||
               lower.Contains("modify") || lower.Contains("update") ||
               lower.Contains("edit") || lower.Contains("refactor") ||
               lower.Contains(".cs") || lower.Contains(".csproj") ||
               lower.Contains("code") || lower.Contains("class") ||
               lower.Contains("function") || lower.Contains("method");
    }

    private static string DetermineCliCommand(string prompt)
    {
        var lower = prompt.ToLower();
        if (lower.Contains("explain") || lower.Contains("what does") || lower.Contains("how does"))
            return "explain";
        return "suggest";
    }

    private static string BuildCliArguments(string command, string prompt)
    {
        var sb = new StringBuilder();
        sb.Append($"copilot {command}");
        var escapedPrompt = prompt.Replace("\"", "\\\"");
        sb.Append($" \"{escapedPrompt}\"");
        if (command == "suggest")
            sb.Append(" --target shell");
        return sb.ToString();
    }

    private async Task<bool> CheckCliAvailableAsync(CancellationToken ct)
    {
        try
        {
            var ghResult = await RunCommandAsync("gh", "--version", ct: ct);
            if (!ghResult.Success) return false;

            var copilotResult = await RunCommandAsync("gh", "copilot --help", ct: ct);
            if (!copilotResult.Success)
            {
                _logger?.LogWarning(
                    "GitHub Copilot CLI extension not found. " +
                    "Install with: gh extension install github/gh-copilot");
            }
            return copilotResult.Success;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "CLI availability check failed");
            return false;
        }
    }

    private static async Task<(bool Success, string Output, string? Error)> RunCommandAsync(
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
                return (false, "", "Failed to start process");

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
        return text.Length / 4;
    }

    public void Dispose()
    {
        _apiClient?.Dispose();
        GC.SuppressFinalize(this);
    }
}

// ============================================================================
// Options
// ============================================================================

public class GitHubCopilotOptions
{
    /// <summary>Default model for API requests. Uses publisher/model format per GitHub Models API.</summary>
    public string DefaultModel { get; set; } = "openai/gpt-4o";

    /// <summary>GitHub token for API auth. Falls back to GITHUB_TOKEN / GH_TOKEN env vars.
    /// Token must have 'models:read' scope for fine-grained PATs.</summary>
    public string? GitHubToken { get; set; }

    /// <summary>Base URL for the GitHub Models API.</summary>
    public string ApiBaseUrl { get; set; } = "https://models.github.ai/";

    /// <summary>Default max tokens for completions.</summary>
    public int DefaultMaxTokens { get; set; } = 4096;

    /// <summary>Temperature for completions. Low = deterministic coding output.</summary>
    public double Temperature { get; set; } = 0.1;

    /// <summary>Max retries on transient errors (429, 5xx).</summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>Request timeout in minutes.</summary>
    public int TimeoutMinutes { get; set; } = 5;
}
