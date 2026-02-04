using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace HumanLayerAutomation;

/// <summary>
/// Client for invoking Claude Code CLI directly.
/// No daemon required - executes claude commands as child processes.
/// </summary>
public class ClaudeCodeClient : IDisposable
{
    private readonly ILogger<ClaudeCodeClient>? _logger;
    private readonly string _claudePath;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly List<Process> _runningProcesses = [];

    public string DefaultWorkingDir { get; set; }
    public string? DefaultModel { get; set; }

    public ClaudeCodeClient(
        string? claudePath = null,
        string? defaultWorkingDir = null,
        ILogger<ClaudeCodeClient>? logger = null)
    {
        _claudePath = claudePath ?? "claude";
        DefaultWorkingDir = defaultWorkingDir ?? Environment.CurrentDirectory;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true
        };
    }

    // ========================================================================
    // Health & System
    // ========================================================================

    /// <summary>Check if Claude CLI is available and working.</summary>
    public async Task<ClaudeVersionInfo> GetVersionAsync(CancellationToken ct = default)
    {
        var result = await RunCommandAsync(["--version"], ct: ct);

        // Parse version output (e.g., "claude 1.0.0")
        var version = result.Output.Trim();
        return new ClaudeVersionInfo
        {
            Version = version,
            IsAvailable = result.ExitCode == 0
        };
    }

    /// <summary>Check if Claude CLI is installed and accessible.</summary>
    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            var info = await GetVersionAsync(ct);
            return info.IsAvailable;
        }
        catch
        {
            return false;
        }
    }

    // ========================================================================
    // Task Execution - Print Mode (Non-Interactive)
    // ========================================================================

    /// <summary>
    /// Run a prompt in print mode (non-interactive).
    /// Returns the result directly without streaming.
    /// </summary>
    public async Task<ClaudeResult> RunAsync(
        string prompt,
        ClaudeOptions? options = null,
        CancellationToken ct = default)
    {
        options ??= new ClaudeOptions();
        var args = BuildArguments(prompt, options, printMode: true);

        _logger?.LogInformation("Running Claude: {Prompt}", Truncate(prompt, 50));

        var startTime = DateTime.UtcNow;
        var result = await RunCommandAsync(
            args,
            workingDir: options.WorkingDir ?? DefaultWorkingDir,
            ct: ct);

        var duration = DateTime.UtcNow - startTime;

        // Try to parse JSON output if format was json
        ClaudeJsonResult? jsonResult = null;
        if (options.OutputFormat == "json" && !string.IsNullOrEmpty(result.Output))
        {
            try
            {
                jsonResult = JsonSerializer.Deserialize<ClaudeJsonResult>(result.Output, _jsonOptions);
            }
            catch (JsonException ex)
            {
                _logger?.LogWarning("Failed to parse JSON output: {Message}", ex.Message);
            }
        }

        return new ClaudeResult
        {
            Success = result.ExitCode == 0,
            ExitCode = result.ExitCode,
            Output = result.Output,
            Error = result.Error,
            Duration = duration,
            JsonResult = jsonResult,
            CostUsd = jsonResult?.CostUsd,
            InputTokens = jsonResult?.InputTokens,
            OutputTokens = jsonResult?.OutputTokens,
            SessionId = jsonResult?.SessionId
        };
    }

    /// <summary>
    /// Run a prompt with real-time streaming output.
    /// </summary>
    public async Task<ClaudeResult> RunStreamingAsync(
        string prompt,
        Action<string> onOutput,
        ClaudeOptions? options = null,
        CancellationToken ct = default)
    {
        options ??= new ClaudeOptions();
        var args = BuildArguments(prompt, options, printMode: true);

        _logger?.LogInformation("Running Claude (streaming): {Prompt}", Truncate(prompt, 50));

        var startTime = DateTime.UtcNow;
        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();

        var psi = new ProcessStartInfo
        {
            FileName = _claudePath,
            Arguments = string.Join(" ", args.Select(EscapeArgument)),
            WorkingDirectory = options.WorkingDir ?? DefaultWorkingDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };
        process.Start();

        lock (_runningProcesses)
        {
            _runningProcesses.Add(process);
        }

        try
        {
            // Read output asynchronously with streaming
            var outputTask = Task.Run(async () =>
            {
                var buffer = new char[256];
                while (!ct.IsCancellationRequested)
                {
                    var bytesRead = await process.StandardOutput.ReadAsync(buffer, ct);
                    if (bytesRead == 0) break;

                    var text = new string(buffer, 0, bytesRead);
                    outputBuilder.Append(text);
                    onOutput(text);
                }
            }, ct);

            var errorTask = process.StandardError.ReadToEndAsync(ct);

            await process.WaitForExitAsync(ct);
            await Task.WhenAll(outputTask, errorTask);

            errorBuilder.Append(await errorTask);

            var duration = DateTime.UtcNow - startTime;

            return new ClaudeResult
            {
                Success = process.ExitCode == 0,
                ExitCode = process.ExitCode,
                Output = outputBuilder.ToString(),
                Error = errorBuilder.ToString(),
                Duration = duration
            };
        }
        finally
        {
            lock (_runningProcesses)
            {
                _runningProcesses.Remove(process);
            }
        }
    }

    // ========================================================================
    // Task Execution - High-Level API
    // ========================================================================

    /// <summary>
    /// Run an automation task with full configuration.
    /// </summary>
    public async Task<TaskResult> RunTaskAsync(AutomationTask task, CancellationToken ct = default)
    {
        var startTime = DateTime.UtcNow;

        var options = new ClaudeOptions
        {
            WorkingDir = task.WorkingDir ?? DefaultWorkingDir,
            Model = task.Model ?? DefaultModel,
            MaxTurns = task.MaxTurns,
            AllowedTools = task.AllowedTools,
            AutoApprove = task.AutoApprove,
            OutputFormat = "json"
        };

        try
        {
            var result = await RunAsync(task.Query, options, ct);

            return new TaskResult
            {
                TaskName = task.Name,
                SessionId = result.SessionId ?? "",
                Status = result.Success ? "completed" : "failed",
                Duration = result.Duration,
                CostUsd = result.CostUsd,
                InputTokens = result.InputTokens,
                OutputTokens = result.OutputTokens,
                Summary = ExtractSummary(result.Output),
                ErrorMessage = result.Success ? null : result.Error
            };
        }
        catch (Exception ex)
        {
            return new TaskResult
            {
                TaskName = task.Name,
                SessionId = "",
                Status = "error",
                Duration = DateTime.UtcNow - startTime,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// Continue an existing session with a follow-up prompt.
    /// </summary>
    public async Task<ClaudeResult> ContinueSessionAsync(
        string sessionId,
        string prompt,
        ClaudeOptions? options = null,
        CancellationToken ct = default)
    {
        options ??= new ClaudeOptions();
        options = options with { ResumeSessionId = sessionId };

        return await RunAsync(prompt, options, ct);
    }

    // ========================================================================
    // Conversation Mode (Interactive - for special use cases)
    // ========================================================================

    /// <summary>
    /// Start an interactive conversation process.
    /// Returns a handle to send messages and receive responses.
    /// </summary>
    public ClaudeConversation StartConversation(ClaudeOptions? options = null)
    {
        options ??= new ClaudeOptions();

        var psi = new ProcessStartInfo
        {
            FileName = _claudePath,
            WorkingDirectory = options.WorkingDir ?? DefaultWorkingDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true
        };

        // Add model and other options
        if (!string.IsNullOrEmpty(options.Model))
        {
            psi.ArgumentList.Add("--model");
            psi.ArgumentList.Add(options.Model);
        }

        if (options.AutoApprove)
        {
            psi.ArgumentList.Add("--dangerously-skip-permissions");
        }

        var process = new Process { StartInfo = psi };
        process.Start();

        lock (_runningProcesses)
        {
            _runningProcesses.Add(process);
        }

        return new ClaudeConversation(process, _logger);
    }

    // ========================================================================
    // Private Helpers
    // ========================================================================

    private List<string> BuildArguments(string prompt, ClaudeOptions options, bool printMode)
    {
        var args = new List<string>();

        // Print mode for non-interactive execution
        // The prompt follows --print as a positional argument: claude --print "prompt"
        if (printMode)
        {
            args.Add("--print");
            args.Add(prompt);  // Prompt is positional argument after --print
        }

        // Output format
        if (!string.IsNullOrEmpty(options.OutputFormat))
        {
            args.Add("--output-format");
            args.Add(options.OutputFormat);
        }

        // Model selection
        var model = options.Model ?? DefaultModel;
        if (!string.IsNullOrEmpty(model))
        {
            args.Add("--model");
            args.Add(model);
        }

        // Max turns
        if (options.MaxTurns.HasValue)
        {
            args.Add("--max-turns");
            args.Add(options.MaxTurns.Value.ToString());
        }

        // Auto-approve (skip permissions)
        if (options.AutoApprove)
        {
            args.Add("--dangerously-skip-permissions");
        }

        // Allowed tools
        if (options.AllowedTools?.Count > 0)
        {
            args.Add("--allowedTools");
            args.Add(string.Join(",", options.AllowedTools));
        }

        // Disallowed tools
        if (options.DisallowedTools?.Count > 0)
        {
            args.Add("--disallowedTools");
            args.Add(string.Join(",", options.DisallowedTools));
        }

        // System prompt
        if (!string.IsNullOrEmpty(options.SystemPrompt))
        {
            args.Add("--system-prompt");
            args.Add(options.SystemPrompt);
        }

        // Append system prompt
        if (!string.IsNullOrEmpty(options.AppendSystemPrompt))
        {
            args.Add("--append-system-prompt");
            args.Add(options.AppendSystemPrompt);
        }

        // Resume session
        if (!string.IsNullOrEmpty(options.ResumeSessionId))
        {
            args.Add("--resume");
            args.Add(options.ResumeSessionId);
        }

        return args;
    }

    private async Task<CommandResult> RunCommandAsync(
        IEnumerable<string> args,
        string? workingDir = null,
        CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _claudePath,
            WorkingDirectory = workingDir ?? DefaultWorkingDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = psi };

        _logger?.LogDebug("Executing: {FileName} {Args}",
            psi.FileName,
            string.Join(" ", psi.ArgumentList));

        process.Start();

        var outputTask = process.StandardOutput.ReadToEndAsync(ct);
        var errorTask = process.StandardError.ReadToEndAsync(ct);

        await process.WaitForExitAsync(ct);

        return new CommandResult
        {
            ExitCode = process.ExitCode,
            Output = await outputTask,
            Error = await errorTask
        };
    }

    private static string EscapeArgument(string arg)
    {
        if (string.IsNullOrEmpty(arg)) return "\"\"";
        if (!arg.Contains(' ') && !arg.Contains('"')) return arg;
        return $"\"{arg.Replace("\"", "\\\"")}\"";
    }

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text)) return "";
        text = text.Replace("\n", " ").Replace("\r", "");
        return text.Length <= maxLength ? text : text[..(maxLength - 3)] + "...";
    }

    private static string? ExtractSummary(string output)
    {
        if (string.IsNullOrEmpty(output)) return null;

        // For JSON output, try to extract result
        if (output.TrimStart().StartsWith('{'))
        {
            try
            {
                using var doc = JsonDocument.Parse(output);
                if (doc.RootElement.TryGetProperty("result", out var result))
                {
                    return result.GetString();
                }
            }
            catch { }
        }

        // Return first 200 chars as summary
        return Truncate(output, 200);
    }

    public void Dispose()
    {
        // Kill any running processes
        lock (_runningProcesses)
        {
            foreach (var process in _runningProcesses)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    process.Dispose();
                }
                catch { }
            }
            _runningProcesses.Clear();
        }
        GC.SuppressFinalize(this);
    }

    private record CommandResult
    {
        public int ExitCode { get; init; }
        public string Output { get; init; } = "";
        public string Error { get; init; } = "";
    }
}

/// <summary>
/// Represents an interactive conversation with Claude.
/// </summary>
public class ClaudeConversation : IDisposable
{
    private readonly Process _process;
    private readonly ILogger? _logger;
    private bool _disposed;

    internal ClaudeConversation(Process process, ILogger? logger)
    {
        _process = process;
        _logger = logger;
    }

    /// <summary>Send a message and get the response.</summary>
    public async Task<string> SendAsync(string message, CancellationToken ct = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ClaudeConversation));

        await _process.StandardInput.WriteLineAsync(message.AsMemory(), ct);
        await _process.StandardInput.FlushAsync();

        // Read until we get a complete response
        var response = new StringBuilder();
        var buffer = new char[1024];

        while (!ct.IsCancellationRequested)
        {
            var bytesRead = await _process.StandardOutput.ReadAsync(buffer, ct);
            if (bytesRead == 0) break;

            response.Append(buffer, 0, bytesRead);

            // Check if response is complete (heuristic: ends with newline after content)
            var text = response.ToString();
            if (text.EndsWith("\n\n") || text.EndsWith("\r\n\r\n"))
            {
                break;
            }
        }

        return response.ToString().Trim();
    }

    /// <summary>Check if the conversation is still active.</summary>
    public bool IsActive => !_disposed && !_process.HasExited;

    /// <summary>End the conversation.</summary>
    public void End()
    {
        if (_disposed) return;

        try
        {
            _process.StandardInput.WriteLine("/exit");
            _process.WaitForExit(5000);

            if (!_process.HasExited)
            {
                _process.Kill();
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning("Error ending conversation: {Message}", ex.Message);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        End();
        _process.Dispose();
        GC.SuppressFinalize(this);
    }
}
