using System.Text.Json.Serialization;

namespace HumanLayerAutomation;

// ============================================================================
// Session Models
// ============================================================================

public record CreateSessionRequest
{
    [JsonPropertyName("query")]
    public required string Query { get; init; }

    [JsonPropertyName("working_dir")]
    public string? WorkingDir { get; init; }

    [JsonPropertyName("model")]
    public string? Model { get; init; } // "opus", "sonnet", "haiku"

    [JsonPropertyName("max_turns")]
    public int? MaxTurns { get; init; }

    [JsonPropertyName("auto_accept_edits")]
    public bool? AutoAcceptEdits { get; init; }

    [JsonPropertyName("dangerously_skip_permissions")]
    public bool? DangerouslySkipPermissions { get; init; }

    [JsonPropertyName("dangerously_skip_permissions_timeout")]
    public long? DangerouslySkipPermissionsTimeoutMs { get; init; }

    [JsonPropertyName("system_prompt")]
    public string? SystemPrompt { get; init; }

    [JsonPropertyName("append_system_prompt")]
    public string? AppendSystemPrompt { get; init; }

    [JsonPropertyName("allowed_tools")]
    public List<string>? AllowedTools { get; init; }

    [JsonPropertyName("disallowed_tools")]
    public List<string>? DisallowedTools { get; init; }

    // Proxy configuration for OpenRouter/other providers
    [JsonPropertyName("proxy_enabled")]
    public bool? ProxyEnabled { get; init; }

    [JsonPropertyName("proxy_base_url")]
    public string? ProxyBaseUrl { get; init; }

    [JsonPropertyName("proxy_model_override")]
    public string? ProxyModelOverride { get; init; }

    [JsonPropertyName("proxy_api_key")]
    public string? ProxyApiKey { get; init; }

    [JsonPropertyName("mcp_config")]
    public McpConfig? McpConfig { get; init; }

    [JsonPropertyName("permission_prompt_tool")]
    public string? PermissionPromptTool { get; init; }
}

public record McpConfig
{
    [JsonPropertyName("mcpServers")]
    public Dictionary<string, McpServer>? McpServers { get; init; }
}

public record McpServer
{
    [JsonPropertyName("command")]
    public string? Command { get; init; }

    [JsonPropertyName("args")]
    public List<string>? Args { get; init; }

    [JsonPropertyName("env")]
    public Dictionary<string, string>? Env { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("url")]
    public string? Url { get; init; }
}

public record CreateSessionResponse
{
    [JsonPropertyName("data")]
    public required CreateSessionData Data { get; init; }
}

public record CreateSessionData
{
    [JsonPropertyName("session_id")]
    public required string SessionId { get; init; }

    [JsonPropertyName("run_id")]
    public required string RunId { get; init; }
}

public record Session
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("run_id")]
    public string? RunId { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("query")]
    public string? Query { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("summary")]
    public string? Summary { get; init; }

    [JsonPropertyName("model")]
    public string? Model { get; init; }

    [JsonPropertyName("working_dir")]
    public string? WorkingDir { get; init; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; init; }

    [JsonPropertyName("last_activity_at")]
    public DateTime? LastActivityAt { get; init; }

    [JsonPropertyName("completed_at")]
    public DateTime? CompletedAt { get; init; }

    [JsonPropertyName("error_message")]
    public string? ErrorMessage { get; init; }

    [JsonPropertyName("cost_usd")]
    public decimal? CostUsd { get; init; }

    [JsonPropertyName("input_tokens")]
    public int? InputTokens { get; init; }

    [JsonPropertyName("output_tokens")]
    public int? OutputTokens { get; init; }

    [JsonPropertyName("duration_ms")]
    public long? DurationMs { get; init; }

    [JsonPropertyName("archived")]
    public bool Archived { get; init; }

    [JsonPropertyName("auto_accept_edits")]
    public bool AutoAcceptEdits { get; init; }

    [JsonPropertyName("dangerously_skip_permissions")]
    public bool DangerouslySkipPermissions { get; init; }
}

public record SessionResponse
{
    [JsonPropertyName("data")]
    public required Session Data { get; init; }
}

public record SessionsResponse
{
    [JsonPropertyName("data")]
    public required List<Session> Data { get; init; }

    [JsonPropertyName("counts")]
    public SessionCounts? Counts { get; init; }
}

public record SessionCounts
{
    [JsonPropertyName("normal")]
    public int Normal { get; init; }

    [JsonPropertyName("archived")]
    public int Archived { get; init; }

    [JsonPropertyName("draft")]
    public int Draft { get; init; }
}

public record ContinueSessionRequest
{
    [JsonPropertyName("query")]
    public required string Query { get; init; }
}

public record ContinueSessionResponse
{
    [JsonPropertyName("data")]
    public required ContinueSessionData Data { get; init; }
}

public record ContinueSessionData
{
    [JsonPropertyName("session_id")]
    public required string SessionId { get; init; }

    [JsonPropertyName("run_id")]
    public required string RunId { get; init; }

    [JsonPropertyName("parent_session_id")]
    public string? ParentSessionId { get; init; }
}

// ============================================================================
// Approval Models
// ============================================================================

public record Approval
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("run_id")]
    public string? RunId { get; init; }

    [JsonPropertyName("session_id")]
    public string? SessionId { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; } // "pending", "approved", "denied"

    [JsonPropertyName("tool_name")]
    public string? ToolName { get; init; }

    [JsonPropertyName("tool_input")]
    public Dictionary<string, object>? ToolInput { get; init; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; init; }

    [JsonPropertyName("responded_at")]
    public DateTime? RespondedAt { get; init; }

    [JsonPropertyName("comment")]
    public string? Comment { get; init; }
}

public record ApprovalResponse
{
    [JsonPropertyName("data")]
    public required Approval Data { get; init; }
}

public record ApprovalsResponse
{
    [JsonPropertyName("data")]
    public required List<Approval> Data { get; init; }
}

public record DecideApprovalRequest
{
    [JsonPropertyName("decision")]
    public required string Decision { get; init; } // "approve" or "deny"

    [JsonPropertyName("comment")]
    public string? Comment { get; init; }
}

public record DecideApprovalResponse
{
    [JsonPropertyName("data")]
    public required DecideApprovalData Data { get; init; }
}

public record DecideApprovalData
{
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }
}

// ============================================================================
// Conversation Models
// ============================================================================

public record ConversationEvent
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("session_id")]
    public string? SessionId { get; init; }

    [JsonPropertyName("sequence")]
    public int Sequence { get; init; }

    [JsonPropertyName("event_type")]
    public string? EventType { get; init; } // "message", "tool_call", "tool_result"

    [JsonPropertyName("role")]
    public string? Role { get; init; } // "user", "assistant", "system"

    [JsonPropertyName("content")]
    public string? Content { get; init; }

    [JsonPropertyName("tool_name")]
    public string? ToolName { get; init; }

    [JsonPropertyName("tool_input_json")]
    public string? ToolInputJson { get; init; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; init; }
}

public record ConversationResponse
{
    [JsonPropertyName("data")]
    public required List<ConversationEvent> Data { get; init; }
}

// ============================================================================
// Health & System Models
// ============================================================================

public record HealthResponse
{
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("version")]
    public string? Version { get; init; }
}

// ============================================================================
// SSE Event Models
// ============================================================================

public record SseEvent
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; init; }

    [JsonPropertyName("data")]
    public Dictionary<string, object>? Data { get; init; }
}

// ============================================================================
// Task Definition for Automation
// ============================================================================

public record AutomationTask
{
    public required string Name { get; init; }
    public required string Query { get; init; }
    public string? WorkingDir { get; init; }
    public string? Model { get; init; }
    public int? MaxTurns { get; init; }
    public bool AutoApprove { get; init; }
    public TimeSpan? AutoApproveTimeout { get; init; }
    public List<string>? AllowedTools { get; init; }

    // OpenRouter/alternative provider settings
    public string? ProxyBaseUrl { get; init; }
    public string? ProxyModel { get; init; }
    public string? ProxyApiKey { get; init; }
}

public record TaskResult
{
    public required string TaskName { get; init; }
    public required string SessionId { get; init; }
    public required string Status { get; init; }
    public TimeSpan Duration { get; init; }
    public decimal? CostUsd { get; init; }
    public int? InputTokens { get; init; }
    public int? OutputTokens { get; init; }
    public string? ErrorMessage { get; init; }
    public string? Summary { get; init; }
}
