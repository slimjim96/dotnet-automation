# API Reference

Complete reference for the HumanLayer daemon REST API and .NET client methods.

## Base URL

```
http://localhost:7777/api/v1
```

Configure via environment variable:
```bash
export HUMANLAYER_URL="http://localhost:7777/api/v1"
```

## Authentication

The daemon uses filesystem-based security. No authentication tokens are required for local connections. For remote access, consider:

- Running behind a reverse proxy with authentication
- Using SSH tunneling
- Network-level access controls

---

## Health Endpoints

### GET /health

Check if the daemon is running and healthy.

**Response**:
```json
{
  "status": "ok",
  "version": "0.1.0",
  "dependencies": {
    "claude": {
      "available": true,
      "path": "/usr/local/bin/claude",
      "version": "1.0.110"
    }
  }
}
```

**.NET Client**:
```csharp
var health = await client.HealthAsync();
Console.WriteLine($"Status: {health.Status}");
```

---

## Session Endpoints

### POST /sessions

Create and launch a new AI session.

**Request Body**:
```json
{
  "query": "Help me refactor the authentication module",
  "working_dir": "/home/user/project",
  "model": "sonnet",
  "max_turns": 50,
  "auto_accept_edits": false,
  "dangerously_skip_permissions": false,
  "dangerously_skip_permissions_timeout": 300000,
  "system_prompt": "You are a code review expert",
  "append_system_prompt": "Focus on security issues",
  "allowed_tools": ["Read", "Write", "Edit"],
  "disallowed_tools": ["Bash"],
  "proxy_enabled": false,
  "proxy_base_url": "https://openrouter.ai/api/v1",
  "proxy_model_override": "openai/gpt-4-turbo",
  "proxy_api_key": "sk-or-...",
  "mcp_config": {
    "mcpServers": {
      "approvals": {
        "command": "hlyr",
        "args": ["mcp", "claude_approvals"]
      }
    }
  },
  "permission_prompt_tool": "mcp__approvals__request_permission"
}
```

**Response** (201):
```json
{
  "data": {
    "session_id": "sess_abc123",
    "run_id": "run_xyz789"
  }
}
```

**.NET Client**:
```csharp
var session = await client.CreateSessionAsync(new CreateSessionRequest
{
    Query = "Refactor authentication module",
    WorkingDir = "/home/user/project",
    Model = "sonnet",
    MaxTurns = 50
});
Console.WriteLine($"Session ID: {session.SessionId}");
```

---

### GET /sessions

List all sessions with optional filtering.

**Query Parameters**:

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `leavesOnly` | boolean | `true` | Only return leaf sessions (no children) |
| `filter` | string | - | Filter: `normal`, `archived`, `draft` |

**Response**:
```json
{
  "data": [
    {
      "id": "sess_abc123",
      "run_id": "run_xyz789",
      "status": "running",
      "query": "Refactor auth module",
      "title": "Auth Refactor",
      "model": "sonnet",
      "working_dir": "/home/user/project",
      "created_at": "2024-01-15T10:30:00Z",
      "last_activity_at": "2024-01-15T10:35:00Z",
      "cost_usd": 0.05,
      "input_tokens": 1500,
      "output_tokens": 800
    }
  ],
  "counts": {
    "normal": 5,
    "archived": 12,
    "draft": 2
  }
}
```

**.NET Client**:
```csharp
// All active sessions
var sessions = await client.ListSessionsAsync();

// Include archived
var allSessions = await client.ListSessionsAsync(leafOnly: false, filter: null);

// Only archived
var archived = await client.ListSessionsAsync(filter: "archived");
```

---

### GET /sessions/{id}

Get detailed information about a specific session.

**Response**:
```json
{
  "data": {
    "id": "sess_abc123",
    "run_id": "run_xyz789",
    "claude_session_id": "claude_sess_456",
    "parent_session_id": null,
    "status": "completed",
    "query": "Refactor auth module",
    "summary": "Refactored authentication to use JWT tokens",
    "model": "sonnet",
    "working_dir": "/home/user/project",
    "created_at": "2024-01-15T10:30:00Z",
    "completed_at": "2024-01-15T10:45:00Z",
    "cost_usd": 0.12,
    "input_tokens": 3500,
    "output_tokens": 1800,
    "duration_ms": 900000,
    "auto_accept_edits": false,
    "dangerously_skip_permissions": false,
    "archived": false
  }
}
```

**.NET Client**:
```csharp
var session = await client.GetSessionAsync("sess_abc123");
Console.WriteLine($"Status: {session.Status}, Cost: ${session.CostUsd}");
```

---

### POST /sessions/{id}/continue

Create a child session that continues from an existing session.

**Request Body**:
```json
{
  "query": "Now add unit tests for the refactored code"
}
```

**Response** (201):
```json
{
  "data": {
    "session_id": "sess_child456",
    "run_id": "run_child789",
    "claude_session_id": "claude_sess_child",
    "parent_session_id": "sess_abc123"
  }
}
```

**.NET Client**:
```csharp
var child = await client.ContinueSessionAsync("sess_abc123", "Add unit tests");
Console.WriteLine($"Child session: {child.SessionId}");
```

---

### POST /sessions/{id}/interrupt

Interrupt a running session gracefully.

**Response**:
```json
{
  "data": {
    "success": true,
    "session_id": "sess_abc123",
    "status": "interrupting"
  }
}
```

**.NET Client**:
```csharp
await client.InterruptSessionAsync("sess_abc123");
```

---

### GET /sessions/{id}/messages

Get the full conversation history for a session.

**Response**:
```json
{
  "data": [
    {
      "id": 1,
      "session_id": "sess_abc123",
      "sequence": 1,
      "event_type": "message",
      "role": "user",
      "content": "Refactor the authentication module",
      "created_at": "2024-01-15T10:30:00Z"
    },
    {
      "id": 2,
      "session_id": "sess_abc123",
      "sequence": 2,
      "event_type": "message",
      "role": "assistant",
      "content": "I'll analyze the current authentication code...",
      "created_at": "2024-01-15T10:30:05Z"
    },
    {
      "id": 3,
      "session_id": "sess_abc123",
      "sequence": 3,
      "event_type": "tool_call",
      "tool_id": "tool_use_123",
      "tool_name": "Read",
      "tool_input_json": "{\"file_path\": \"/src/auth.ts\"}",
      "created_at": "2024-01-15T10:30:10Z"
    }
  ]
}
```

**.NET Client**:
```csharp
var messages = await client.GetConversationAsync("sess_abc123");
foreach (var msg in messages)
{
    Console.WriteLine($"[{msg.EventType}] {msg.Role}: {msg.Content}");
}
```

---

## Approval Endpoints

### GET /approvals

List all approval requests, optionally filtered by session.

**Query Parameters**:

| Parameter | Type | Description |
|-----------|------|-------------|
| `sessionId` | string | Filter by session ID |

**Response**:
```json
{
  "data": [
    {
      "id": "appr_abc123",
      "run_id": "run_xyz789",
      "session_id": "sess_def456",
      "status": "pending",
      "tool_name": "Bash",
      "tool_input": {
        "command": "npm install express"
      },
      "created_at": "2024-01-15T10:35:00Z",
      "responded_at": null,
      "comment": null
    }
  ]
}
```

**.NET Client**:
```csharp
// All approvals
var approvals = await client.ListApprovalsAsync();

// Only pending
var pending = await client.GetPendingApprovalsAsync();

// For specific session
var sessionApprovals = await client.ListApprovalsAsync(sessionId: "sess_def456");
```

---

### GET /approvals/{id}

Get detailed information about a specific approval.

**Response**:
```json
{
  "data": {
    "id": "appr_abc123",
    "run_id": "run_xyz789",
    "session_id": "sess_def456",
    "status": "pending",
    "tool_name": "Bash",
    "tool_input": {
      "command": "npm install express",
      "description": "Install Express.js web framework"
    },
    "created_at": "2024-01-15T10:35:00Z"
  }
}
```

**.NET Client**:
```csharp
var approval = await client.GetApprovalAsync("appr_abc123");
Console.WriteLine($"Tool: {approval.ToolName}");
Console.WriteLine($"Input: {JsonSerializer.Serialize(approval.ToolInput)}");
```

---

### POST /approvals/{id}/decide

Make a decision on an approval request.

**Request Body**:
```json
{
  "decision": "approve",
  "comment": "Looks safe to proceed"
}
```

Or to deny:
```json
{
  "decision": "deny",
  "comment": "Use a specific version instead: npm install express@4.18.2"
}
```

**Response**:
```json
{
  "data": {
    "success": true
  }
}
```

**.NET Client**:
```csharp
// Approve
await client.ApproveAsync("appr_abc123", "Approved for installation");

// Deny with reason
await client.DenyAsync("appr_abc123", "Use specific version");

// Generic decision
await client.DecideApprovalAsync("appr_abc123", "approve", "OK");
```

---

## Event Streaming

### GET /stream/events

Subscribe to real-time events via Server-Sent Events (SSE).

**Query Parameters**:

| Parameter | Type | Description |
|-----------|------|-------------|
| `eventTypes` | string[] | Filter by event types |
| `sessionId` | string | Filter by session ID |
| `runId` | string | Filter by run ID |

**Event Types**:
- `new_approval` - Approval request created
- `approval_resolved` - Approval decision made
- `session_status_changed` - Session state transition
- `conversation_updated` - New message or tool event

**Event Format**:
```
data: {"type":"new_approval","timestamp":"2024-01-15T10:35:00Z","data":{"approval_id":"appr_abc123","session_id":"sess_def456","tool_name":"Bash"}}

data: {"type":"session_status_changed","timestamp":"2024-01-15T10:36:00Z","data":{"session_id":"sess_def456","old_status":"running","new_status":"waiting_input"}}
```

**.NET Client**:
```csharp
await client.SubscribeToEventsAsync(
    onEvent: evt => {
        Console.WriteLine($"[{evt.Type}] {JsonSerializer.Serialize(evt.Data)}");
    },
    eventTypes: new[] { "new_approval", "session_status_changed" },
    sessionId: "sess_def456"
);
```

---

## .NET Client Reference

### Constructor

```csharp
public HumanLayerClient(
    string baseUrl = "http://localhost:7777/api/v1",
    ILogger<HumanLayerClient>? logger = null
)
```

### Session Methods

| Method | Description |
|--------|-------------|
| `CreateSessionAsync(request)` | Launch new session |
| `GetSessionAsync(sessionId)` | Get session details |
| `ListSessionsAsync(leafOnly?, filter?)` | List sessions |
| `ContinueSessionAsync(sessionId, query)` | Create child session |
| `InterruptSessionAsync(sessionId)` | Stop running session |
| `WaitForSessionAsync(sessionId, timeout?)` | Block until complete |
| `GetConversationAsync(sessionId)` | Get message history |

### Approval Methods

| Method | Description |
|--------|-------------|
| `ListApprovalsAsync(sessionId?)` | List all approvals |
| `GetPendingApprovalsAsync(sessionId?)` | List pending only |
| `GetApprovalAsync(approvalId)` | Get approval details |
| `ApproveAsync(approvalId, comment?)` | Approve request |
| `DenyAsync(approvalId, reason)` | Deny request |
| `DecideApprovalAsync(approvalId, decision, comment?)` | Generic decision |

### Event Methods

| Method | Description |
|--------|-------------|
| `SubscribeToEventsAsync(onEvent, eventTypes?, sessionId?, ct?)` | Subscribe to SSE |
| `StopEventSubscription()` | Cancel subscription |

### High-Level Methods

| Method | Description |
|--------|-------------|
| `HealthAsync()` | Check daemon health |
| `WaitForHealthyAsync(timeout)` | Wait for daemon ready |
| `LaunchAndWaitAsync(request, timeout?)` | Launch and block until done |
| `RunTaskAsync(task)` | Run automation task |

---

## Error Handling

### HTTP Status Codes

| Code | Description |
|------|-------------|
| 200 | Success |
| 201 | Created |
| 400 | Bad request (validation error) |
| 404 | Resource not found |
| 422 | Unprocessable entity (e.g., directory not found) |
| 500 | Internal server error |

### Error Response Format

```json
{
  "error": {
    "code": "HLD-102",
    "message": "Session not found",
    "details": {
      "session_id": "sess_invalid"
    }
  }
}
```

### .NET Exception Handling

```csharp
try
{
    var session = await client.GetSessionAsync("sess_invalid");
}
catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
{
    Console.WriteLine("Session not found");
}
catch (HttpRequestException ex)
{
    Console.WriteLine($"API error: {ex.Message}");
}
```

---

## Rate Limits

The daemon does not enforce rate limits. However, consider:

- Claude Code sessions consume API tokens
- Multiple concurrent sessions increase cost
- SSE connections are long-lived (one per subscription)

## Next Steps

- [Automation Patterns](patterns.md) - Best practices for automation
- [Security](security.md) - Security considerations
- [Getting Started](getting-started.md) - Setup guide
