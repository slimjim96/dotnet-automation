# Core Concepts

This document explains the fundamental concepts and terminology used throughout HumanLayer and this .NET automation client.

## The Problem: AI Agents Need Human Oversight

Modern AI agents are capable of remarkable autonomous work, but they make mistakes. Even a 95% accuracy rate means 1 in 20 actions could be wrong. For high-stakes operations like:

- Executing shell commands
- Modifying production code
- Sending emails on your behalf
- Making API calls with real consequences

...probabilistic accuracy isn't good enough. We need **deterministic human oversight**.

## Function Stakes Framework

HumanLayer categorizes AI actions by their potential impact:

### Low Stakes
- **Examples**: Reading public documentation, analyzing code structure
- **Oversight**: None required, fully automated
- **Tools**: `Read`, `Glob`, `Grep`, `LS`

### Medium Stakes
- **Examples**: Reading private data, accessing internal APIs
- **Oversight**: Audit trail, optional approval
- **Tools**: `WebFetch`, file reads in sensitive directories

### High Stakes
- **Examples**: Writing files, executing commands, sending communications
- **Oversight**: **Mandatory human approval**
- **Tools**: `Write`, `Edit`, `Bash`, `NotebookEdit`

```
┌─────────────────────────────────────────────────────────────┐
│                    Stakes Spectrum                           │
│                                                              │
│   LOW              MEDIUM              HIGH                  │
│   ├────────────────┼────────────────────┤                   │
│   │                │                    │                    │
│   ▼                ▼                    ▼                    │
│ Auto-OK        Audit Trail        Human Approval            │
│                                                              │
└─────────────────────────────────────────────────────────────┘
```

## Key Terminology

### Session

A **session** represents a single AI agent work unit:

```csharp
public record Session
{
    public string Id { get; init; }           // Unique identifier
    public string Status { get; init; }       // Lifecycle state
    public string Query { get; init; }        // Initial task prompt
    public string WorkingDir { get; init; }   // Filesystem context
    public string Model { get; init; }        // AI model used
}
```

**Session Lifecycle States**:

| Status | Description |
|--------|-------------|
| `draft` | Created but not launched |
| `starting` | Claude Code process launching |
| `running` | Actively working on task |
| `waiting_input` | Blocked on approval |
| `completed` | Finished successfully |
| `failed` | Terminated with error |
| `interrupted` | Stopped by user |

```
draft ──▶ starting ──▶ running ◀──┐
                          │       │
                          ▼       │
                   waiting_input ─┘
                          │
              ┌───────────┴───────────┐
              ▼                       ▼
          completed               failed/interrupted
```

### Approval

An **approval** is a request for human authorization before executing a tool:

```csharp
public record Approval
{
    public string Id { get; init; }           // Approval identifier
    public string SessionId { get; init; }    // Parent session
    public string ToolName { get; init; }     // Tool requesting approval
    public object ToolInput { get; init; }    // Parameters to review
    public string Status { get; init; }       // pending/approved/denied
}
```

**The Approval Promise**: When an AI calls a high-stakes tool, the tool itself requests approval. The AI cannot bypass this - the approval is embedded in the tool definition, not a post-hoc check.

### Tool

A **tool** is a capability the AI can invoke:

| Tool Category | Examples | Default Approval |
|---------------|----------|------------------|
| Read-only | `Read`, `Glob`, `Grep` | Auto-approved |
| Write | `Write`, `Edit` | Requires approval |
| Execute | `Bash` | Requires approval |
| Network | `WebFetch`, `WebSearch` | Configurable |

### Model

The **model** determines AI capability and cost:

| Model | Capability | Cost | Use Case |
|-------|------------|------|----------|
| `haiku` | Fast, efficient | $ | Simple tasks, high volume |
| `sonnet` | Balanced | $$ | Standard development work |
| `opus` | Most capable | $$$ | Complex reasoning, critical tasks |

### Proxy

A **proxy** routes AI requests through alternative providers:

```csharp
// Route through OpenRouter to use GPT-4
ProxyEnabled = true,
ProxyBaseUrl = "https://openrouter.ai/api/v1",
ProxyModelOverride = "openai/gpt-4-turbo",
ProxyApiKey = "sk-or-..."
```

Supported providers:
- **OpenRouter**: Gateway to 100+ models
- **Baseten**: Custom model hosting
- **Any OpenAI-compatible API**

## Design Principles

### 1. Deterministic Over Probabilistic

> "Even 90% accuracy means 10% of actions are wrong. For high-stakes operations, we need 100% human verification."

HumanLayer makes approval **deterministic** - built into the tool definition, not a probabilistic filter.

### 2. Local-First Architecture

The daemon runs locally with:
- No external API required for core functionality
- SQLite for persistent storage
- Unix sockets for secure IPC
- Optional cloud features for team collaboration

### 3. Channel Flexibility

Humans can approve via multiple channels:
- **Web UI**: Full-featured dashboard
- **CLI**: Terminal prompts
- **Slack**: Team messaging integration
- **Email**: Async approval workflow

### 4. Automation-Friendly

While maintaining human oversight, the system supports automation:
- Auto-approve safe operations
- Batch processing for efficiency
- Scheduled execution for 24/7 operation
- Parallel task execution for throughput

## Generation Evolution of AI Applications

### Generation 1: Chat (2022-2023)
- Human-initiated Q&A
- No autonomous action
- Single turn interactions

### Generation 2: Agentic Assistants (2023-2024)
- Framework-driven agents (LangChain, AutoGPT)
- Human initiates, agent executes
- Limited autonomy

### Generation 3: Autonomous Agents (2024+)
- Agent-initiated actions
- "Outer loop" orchestration
- Continuous background operation
- **Requires human contact channels** ← HumanLayer addresses this

```
┌────────────────────────────────────────────────────────────┐
│                    Agent Evolution                          │
│                                                             │
│   Gen 1          Gen 2              Gen 3                   │
│   ┌───┐         ┌───────┐          ┌──────────────┐        │
│   │ ? │  ───▶   │  🤖   │   ───▶   │   🤖  ←───┐  │        │
│   └───┘         │ ↑   ↓ │          │    ↕      │  │        │
│   Chat          │ 👤   │          │   👤 ◀───┘  │        │
│                 └───────┘          └──────────────┘        │
│                 Assistants          Autonomous              │
│                                     + Human Loop            │
└────────────────────────────────────────────────────────────┘
```

## MCP (Model Context Protocol)

MCP is the communication protocol between Claude Code and external tools:

```
Claude Code ◀────── MCP ──────▶ Tool Server
            stdio or HTTP      (e.g., hld daemon)
```

**Key MCP Concepts**:
- **Tools**: Functions the AI can call
- **Resources**: Data the AI can access
- **Prompts**: Templates for common operations

The `request_approval` tool is an MCP tool that:
1. Receives tool call from Claude
2. Creates approval in daemon
3. Waits for human decision
4. Returns result to Claude

## Event-Driven Architecture

The system uses events for loose coupling:

### Event Types

| Event | Description | Data |
|-------|-------------|------|
| `new_approval` | Approval created | approval_id, session_id, tool_name |
| `approval_resolved` | Decision made | approval_id, decision |
| `session_status_changed` | State transition | session_id, old_status, new_status |
| `conversation_updated` | New message/tool | session_id, event_type |

### Subscription Example

```csharp
await client.SubscribeToEventsAsync(
    onEvent: evt => {
        switch (evt.Type)
        {
            case "new_approval":
                Console.WriteLine($"New approval: {evt.Data["approval_id"]}");
                break;
            case "session_status_changed":
                Console.WriteLine($"Session {evt.Data["session_id"]}: {evt.Data["new_status"]}");
                break;
        }
    },
    eventTypes: new[] { "new_approval", "session_status_changed" }
);
```

## Auto-Accept Modes

For trusted operations, you can configure automatic approval:

### Auto-Accept Edits

Only auto-approve file modification tools:

```csharp
AutoAcceptEdits = true  // Auto-approve: Write, Edit, NotebookEdit
```

### Dangerously Skip Permissions

Auto-approve ALL tools (use with caution):

```csharp
DangerouslySkipPermissions = true,
DangerouslySkipPermissionsTimeoutMs = 300000  // 5-minute safety timeout
```

### Per-Task Configuration

```csharp
// Safe task: auto-approve everything with timeout
new AutomationTask
{
    Name = "Code Analysis",
    Query = "Analyze code quality",
    AutoApprove = true,
    AutoApproveTimeout = TimeSpan.FromMinutes(5),
    AllowedTools = new[] { "Read", "Glob", "Grep" }  // Whitelist safe tools
}

// Sensitive task: require human approval
new AutomationTask
{
    Name = "Code Refactor",
    Query = "Refactor authentication module",
    AutoApprove = false  // All tool uses need approval
}
```

## Cost Management

### Token Tracking

Each session tracks token usage:

```csharp
public record Session
{
    public int? InputTokens { get; init; }   // Tokens sent to model
    public int? OutputTokens { get; init; }  // Tokens received
    public decimal? CostUsd { get; init; }   // Total cost
}
```

### Cost Optimization Strategies

1. **Model Selection**: Use Haiku for simple tasks
2. **Max Turns**: Limit iterations with `MaxTurns`
3. **Tool Whitelisting**: Restrict to needed tools with `AllowedTools`
4. **Caching**: Claude Code caches context, reducing repeat token costs

## Next Steps

- [Architecture](architecture.md) - System architecture deep dive
- [API Reference](api-reference.md) - Endpoint documentation
- [Patterns](patterns.md) - Automation best practices
