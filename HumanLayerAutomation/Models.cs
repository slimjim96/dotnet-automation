using System.Text.Json.Serialization;

namespace HumanLayerAutomation;

// ============================================================================
// Claude CLI Options
// ============================================================================

/// <summary>
/// Options for configuring Claude CLI execution.
/// </summary>
public record ClaudeOptions
{
    /// <summary>Working directory for the command.</summary>
    public string? WorkingDir { get; init; }

    /// <summary>Model to use: "opus", "sonnet", or "haiku".</summary>
    public string? Model { get; init; }

    /// <summary>Maximum number of agent turns.</summary>
    public int? MaxTurns { get; init; }

    /// <summary>Auto-approve all tool uses (dangerous for production).</summary>
    public bool AutoApprove { get; init; }

    /// <summary>Output format: "text", "json", or "stream-json".</summary>
    public string? OutputFormat { get; init; }

    /// <summary>List of allowed tools (whitelist).</summary>
    public List<string>? AllowedTools { get; init; }

    /// <summary>List of disallowed tools (blacklist).</summary>
    public List<string>? DisallowedTools { get; init; }

    /// <summary>Custom system prompt to use.</summary>
    public string? SystemPrompt { get; init; }

    /// <summary>Additional instructions to append to system prompt.</summary>
    public string? AppendSystemPrompt { get; init; }

    /// <summary>Session ID to resume.</summary>
    public string? ResumeSessionId { get; init; }
}

// ============================================================================
// Claude CLI Results
// ============================================================================

/// <summary>
/// Result from a Claude CLI execution.
/// </summary>
public record ClaudeResult
{
    /// <summary>Whether the command succeeded (exit code 0).</summary>
    public bool Success { get; init; }

    /// <summary>Process exit code.</summary>
    public int ExitCode { get; init; }

    /// <summary>Standard output from the command.</summary>
    public string Output { get; init; } = "";

    /// <summary>Standard error from the command.</summary>
    public string Error { get; init; } = "";

    /// <summary>Duration of the command execution.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>Parsed JSON result (when OutputFormat is "json").</summary>
    public ClaudeJsonResult? JsonResult { get; init; }

    /// <summary>Cost in USD (from JSON output).</summary>
    public decimal? CostUsd { get; init; }

    /// <summary>Input tokens used.</summary>
    public int? InputTokens { get; init; }

    /// <summary>Output tokens generated.</summary>
    public int? OutputTokens { get; init; }

    /// <summary>Session ID for this execution.</summary>
    public string? SessionId { get; init; }

    /// <summary>Whether the response appears to be a question/clarification request rather than completed work.</summary>
    public bool IsQuestioningResponse => ResponseValidator.IsQuestioningResponse(Output, JsonResult);

    /// <summary>Whether the response indicates Claude was blocked by tool permissions (AutoApprove not enabled).</summary>
    public bool IsPermissionRequest => ResponseValidator.IsPermissionRequest(JsonResult?.Result ?? Output);
}

/// <summary>
/// Detects when Claude returns a questioning/clarification response instead of completing work.
/// Used to trigger automatic retry with stronger instructions.
/// </summary>
public static class ResponseValidator
{
    /// <summary>
    /// Phrases that strongly indicate Claude is asking for clarification rather than doing work.
    /// These are checked against the extracted result content, not raw JSON.
    /// </summary>
    private static readonly string[] QuestioningPhrases =
    [
        "could you clarify",
        "could you provide",
        "could you specify",
        "can you clarify",
        "can you provide",
        "can you specify",
        "what would you like",
        "what do you want",
        "what should i",
        "which approach",
        "which option",
        "which method",
        "please clarify",
        "please provide",
        "please specify",
        "i need more information",
        "i need to know",
        "a few questions",
        "some questions",
        "before i proceed",
        "before i can",
        "before proceeding",
        "let me know",
        "do you want me to",
        "would you prefer",
        "would you like me to",
        "shall i",
        "should i"
    ];

    /// <summary>
    /// Phrases that indicate Claude is blocked by tool permissions and asking the user to approve.
    /// This happens when AutoApprove/--dangerously-skip-permissions is not enabled.
    /// </summary>
    private static readonly string[] PermissionRequestPhrases =
    [
        "please approve",
        "approve the",
        "permission to write",
        "permission to create",
        "permission to edit",
        "permission to modify",
        "permissions haven't been granted",
        "permissions have not been granted",
        "write permissions",
        "need permission",
        "need approval",
        "grant permission",
        "grant access",
        "allow me to",
        "i don't have permission",
        "i do not have permission",
        "unable to write",
        "cannot write to",
        "can't write to",
        "access denied",
        "permission denied"
    ];

    /// <summary>
    /// Check if a Claude response looks like a question/clarification request.
    /// Returns true if the response appears to be asking questions rather than completing work.
    /// </summary>
    public static bool IsQuestioningResponse(string? output, ClaudeJsonResult? jsonResult = null)
    {
        // Extract the actual content to analyze
        var content = jsonResult?.Result ?? output;
        if (string.IsNullOrWhiteSpace(content))
            return false;

        // Permission requests are a form of stalling — check those first
        if (IsPermissionRequest(content))
            return true;

        var lower = content.ToLowerInvariant();

        // Check for questioning phrases
        var matchCount = QuestioningPhrases.Count(phrase => lower.Contains(phrase));

        // Single match could be incidental; 2+ is strong signal
        if (matchCount >= 2)
            return true;

        // Check for high question-mark density (many questions in short response)
        var questionMarks = content.Count(c => c == '?');
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;

        // More than 2 question marks and they make up a significant portion of lines
        if (questionMarks >= 3 && lines > 0 && (double)questionMarks / lines > 0.3)
            return true;

        // Response is mostly questions: ends with ? and has questioning phrase
        if (content.TrimEnd().EndsWith('?') && matchCount >= 1)
            return true;

        return false;
    }

    /// <summary>
    /// Check if a Claude response indicates it was blocked by tool permissions.
    /// This is a specific case where --dangerously-skip-permissions was not passed
    /// and Claude couldn't execute Write/Edit/Bash tools.
    /// </summary>
    public static bool IsPermissionRequest(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return false;

        var lower = content.ToLowerInvariant();
        return PermissionRequestPhrases.Any(phrase => lower.Contains(phrase));
    }

    /// <summary>
    /// System prompt instruction that tells Claude to never ask questions during automation.
    /// Append this to all automated/builder invocations.
    /// </summary>
    public const string NoQuestionsPrompt = """
        CRITICAL AUTOMATION INSTRUCTION: You are running in a fully automated pipeline with NO human to answer questions.
        - NEVER ask clarifying questions. NEVER ask the user to choose between options.
        - If you are uncertain, make a reasonable assumption and proceed.
        - If multiple approaches exist, choose the most standard/common one and proceed.
        - If requirements are ambiguous, interpret them in the most straightforward way.
        - Your output must be actionable work product (code, plans, analysis), NOT questions.
        - If you ask a question, the automation will fail and retry, wasting time and cost.
        """;
}

/// <summary>
/// JSON output format from Claude CLI with --output-format json.
/// </summary>
public record ClaudeJsonResult
{
    [JsonPropertyName("session_id")]
    public string? SessionId { get; init; }

    [JsonPropertyName("result")]
    public string? Result { get; init; }

    [JsonPropertyName("is_error")]
    public bool IsError { get; init; }

    [JsonPropertyName("cost_usd")]
    public decimal? CostUsd { get; init; }

    [JsonPropertyName("input_tokens")]
    public int? InputTokens { get; init; }

    [JsonPropertyName("output_tokens")]
    public int? OutputTokens { get; init; }

    [JsonPropertyName("duration_ms")]
    public long? DurationMs { get; init; }

    [JsonPropertyName("num_turns")]
    public int? NumTurns { get; init; }
}

/// <summary>
/// Version information from Claude CLI.
/// </summary>
public record ClaudeVersionInfo
{
    public string Version { get; init; } = "";
    public bool IsAvailable { get; init; }
}

// ============================================================================
// Task Definition for Automation
// ============================================================================

/// <summary>
/// Defines an automation task to run with Claude.
/// </summary>
public record AutomationTask
{
    /// <summary>Human-readable name for the task.</summary>
    public required string Name { get; init; }

    /// <summary>The prompt/query to send to Claude.</summary>
    public required string Query { get; init; }

    /// <summary>Working directory for the task.</summary>
    public string? WorkingDir { get; init; }

    /// <summary>Model to use: "opus", "sonnet", or "haiku".</summary>
    public string? Model { get; init; }

    /// <summary>Maximum number of agent turns.</summary>
    public int? MaxTurns { get; init; }

    /// <summary>Auto-approve all tool uses (dangerous for production).</summary>
    public bool AutoApprove { get; init; }

    /// <summary>Timeout for auto-approve (not used in CLI mode).</summary>
    public TimeSpan? AutoApproveTimeout { get; init; }

    /// <summary>List of allowed tools (whitelist).</summary>
    public List<string>? AllowedTools { get; init; }

    /// <summary>List of disallowed tools (blacklist).</summary>
    public List<string>? DisallowedTools { get; init; }

    /// <summary>Custom system prompt.</summary>
    public string? SystemPrompt { get; init; }
}

/// <summary>
/// Result from running an automation task.
/// </summary>
public record TaskResult
{
    /// <summary>Name of the task that was run.</summary>
    public required string TaskName { get; init; }

    /// <summary>Session ID from Claude (if available).</summary>
    public required string SessionId { get; init; }

    /// <summary>Final status: "completed", "failed", or "error".</summary>
    public required string Status { get; init; }

    /// <summary>Duration of the task execution.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>Cost in USD.</summary>
    public decimal? CostUsd { get; init; }

    /// <summary>Input tokens used.</summary>
    public int? InputTokens { get; init; }

    /// <summary>Output tokens generated.</summary>
    public int? OutputTokens { get; init; }

    /// <summary>Error message if failed.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Summary of the result.</summary>
    public string? Summary { get; init; }
}

// ============================================================================
// Scheduled Task Models
// ============================================================================

/// <summary>
/// A scheduled task with interval and last run tracking.
/// </summary>
public record ScheduledTask
{
    /// <summary>The automation task to run.</summary>
    public required AutomationTask Task { get; init; }

    /// <summary>Interval between runs.</summary>
    public required TimeSpan Interval { get; init; }

    /// <summary>Last time the task was run.</summary>
    public DateTime LastRun { get; set; } = DateTime.MinValue;

    /// <summary>Whether the task is enabled.</summary>
    public bool Enabled { get; set; } = true;
}

// ============================================================================
// Streaming Event Models
// ============================================================================

/// <summary>
/// Event from Claude CLI streaming output.
/// </summary>
public record ClaudeStreamEvent
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "";

    [JsonPropertyName("message")]
    public string? Message { get; init; }

    [JsonPropertyName("tool_name")]
    public string? ToolName { get; init; }

    [JsonPropertyName("tool_input")]
    public Dictionary<string, object>? ToolInput { get; init; }

    [JsonPropertyName("tool_result")]
    public string? ToolResult { get; init; }

    [JsonPropertyName("session_id")]
    public string? SessionId { get; init; }
}

// ============================================================================
// Tool Permission Models (for custom approval logic)
// ============================================================================

/// <summary>
/// Represents a tool permission request.
/// </summary>
public record ToolPermissionRequest
{
    /// <summary>Name of the tool requesting permission.</summary>
    public required string ToolName { get; init; }

    /// <summary>Input parameters for the tool.</summary>
    public Dictionary<string, object>? ToolInput { get; init; }

    /// <summary>Risk level assessment.</summary>
    public ToolRiskLevel RiskLevel { get; init; }
}

/// <summary>
/// Risk level for tool operations.
/// </summary>
public enum ToolRiskLevel
{
    /// <summary>Safe read-only operations (Read, Glob, Grep).</summary>
    Low,

    /// <summary>Operations with audit trail (WebFetch).</summary>
    Medium,

    /// <summary>Dangerous write/execute operations (Bash, Write, Edit).</summary>
    High
}

/// <summary>
/// Tool classification by risk level.
/// </summary>
public static class ToolClassification
{
    public static readonly HashSet<string> LowRiskTools =
    [
        "Read", "Glob", "Grep", "LS", "WebSearch"
    ];

    public static readonly HashSet<string> MediumRiskTools =
    [
        "WebFetch", "Task"
    ];

    public static readonly HashSet<string> HighRiskTools =
    [
        "Bash", "Write", "Edit", "NotebookEdit", "MultiEdit"
    ];

    public static ToolRiskLevel GetRiskLevel(string toolName)
    {
        if (LowRiskTools.Contains(toolName)) return ToolRiskLevel.Low;
        if (MediumRiskTools.Contains(toolName)) return ToolRiskLevel.Medium;
        return ToolRiskLevel.High;
    }
}
