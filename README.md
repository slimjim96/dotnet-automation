# Claude Code .NET Automation

Cross-platform .NET 10 client for automating Claude Code CLI directly - no daemon required.

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

## What is This?

A .NET library and automation framework that invokes Claude Code CLI directly as a subprocess. No intermediate daemon or server required - just the Claude CLI and your .NET code.

**Key Features:**
- Direct CLI invocation via `Process` - no daemon needed
- Run AI tasks programmatically with full control
- Schedule recurring automation tasks
- Run multiple tasks in parallel
- Real-time streaming output
- Tool restrictions for safety

```csharp
// Run an AI task directly
using var client = new ClaudeCodeClient();

var result = await client.RunAsync(
    prompt: "Analyze this codebase for security issues",
    options: new ClaudeOptions
    {
        Model = "sonnet",
        AutoApprove = true,
        AllowedTools = ["Read", "Glob", "Grep"]  // Safe read-only tools
    });

Console.WriteLine($"Result: {result.Output}");
Console.WriteLine($"Cost: ${result.CostUsd}");
```

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    Your .NET Application                     │
│                                                              │
│   ┌──────────────────────────────────────────────────────┐  │
│   │                  ClaudeCodeClient                     │  │
│   │  • Direct Process invocation                         │  │
│   │  • JSON output parsing                               │  │
│   │  • Streaming support                                 │  │
│   └──────────────────────────┬───────────────────────────┘  │
└──────────────────────────────┼──────────────────────────────┘
                               │ Process.Start()
                               ▼
┌──────────────────────────────────────────────────────────────┐
│                   Claude Code CLI                            │
│                   (claude --print ...)                       │
│                                                              │
│   • Read/analyze code     • Execute tools                   │
│   • JSON output format    • Session management              │
└──────────────────────────────────────────────────────────────┘
```

**Previous architecture (deprecated):**
```
.NET App → HTTP REST → HumanLayer Daemon (hld) → Claude Code
```

**New architecture (current):**
```
.NET App → Process.Start() → Claude CLI
```

## Quick Start

### Prerequisites

1. **.NET 10 SDK**: https://dotnet.microsoft.com/download
2. **Claude Code CLI**: `npm install -g @anthropic/claude-code`
3. **Anthropic API Key**: Set `ANTHROPIC_API_KEY` environment variable

### Installation

```bash
# Clone the repository
git clone https://github.com/yourusername/claude-dotnet-automation.git
cd claude-dotnet-automation

# Restore and build
dotnet restore
dotnet build
```

### Run

```bash
cd HumanLayerAutomation

# Run demo
dotnet run -- demo

# Run a single task
dotnet run -- run "List files in this directory"

# Run with auto-approve
AUTO_APPROVE=true dotnet run -- run "Analyze this code"

# Start scheduler
dotnet run -- scheduler

# Run parallel tasks
dotnet run -- parallel

# Streaming output demo
dotnet run -- stream
```

## Automation Modes

### Demo Mode
Quick demonstration of Claude CLI integration:
```bash
dotnet run -- demo
```

### Single Task Runner
Run any prompt as a one-off task:
```bash
dotnet run -- run "Your prompt here"

# With options
CLAUDE_MODEL=opus MAX_TURNS=30 AUTO_APPROVE=true dotnet run -- run "Complex task"
```

### Task Scheduler
Run AI tasks on a schedule (cron-like):
```bash
dotnet run -- scheduler
```
- Hourly code reviews
- 4-hourly security scans
- Daily documentation checks

### Parallel Runner
Execute multiple AI tasks concurrently:
```bash
dotnet run -- parallel
```

### Streaming Demo
Real-time output streaming:
```bash
dotnet run -- stream
```

## Configuration

### Environment Variables

| Variable | Description | Default |
|----------|-------------|---------|
| `CLAUDE_PATH` | Path to claude executable | `claude` |
| `WORKING_DIR` | Default working directory | Current directory |
| `CLAUDE_MODEL` | Default model (opus, sonnet, haiku) | `sonnet` |
| `MAX_TURNS` | Maximum agent turns | `20` |
| `AUTO_APPROVE` | Auto-approve all tools | `false` |
| `ANTHROPIC_API_KEY` | Your Anthropic API key | (required) |

### Setting Environment Variables

**Windows (PowerShell):**
```powershell
$env:ANTHROPIC_API_KEY = "sk-ant-..."
$env:CLAUDE_MODEL = "haiku"
$env:AUTO_APPROVE = "true"
```

**Linux / macOS:**
```bash
export ANTHROPIC_API_KEY="sk-ant-..."
export CLAUDE_MODEL="haiku"
export AUTO_APPROVE="true"
```

## Code Examples

### Basic Task Execution

```csharp
using var client = new ClaudeCodeClient();

var result = await client.RunAsync(
    prompt: "What files are in this directory?",
    options: new ClaudeOptions
    {
        Model = "haiku",
        MaxTurns = 5,
        AutoApprove = true
    });

Console.WriteLine(result.Output);
```

### Safe Read-Only Tasks

```csharp
var result = await client.RunAsync(
    prompt: "Analyze code quality",
    options: new ClaudeOptions
    {
        AutoApprove = true,
        AllowedTools = ["Read", "Glob", "Grep"]  // Only allow safe tools
    });
```

### Task with JSON Output

```csharp
var result = await client.RunAsync(
    prompt: "Summarize this codebase",
    options: new ClaudeOptions
    {
        OutputFormat = "json",
        AutoApprove = true
    });

if (result.JsonResult != null)
{
    Console.WriteLine($"Session: {result.JsonResult.SessionId}");
    Console.WriteLine($"Tokens: {result.JsonResult.InputTokens} in, {result.JsonResult.OutputTokens} out");
}
```

### Streaming Output

```csharp
var result = await client.RunStreamingAsync(
    prompt: "Explain this code step by step",
    onOutput: chunk => Console.Write(chunk),
    options: new ClaudeOptions
    {
        Model = "sonnet",
        AutoApprove = true
    });
```

### Using AutomationTask

```csharp
var task = new AutomationTask
{
    Name = "Security Scan",
    Query = "Scan for security vulnerabilities",
    Model = "sonnet",
    MaxTurns = 20,
    AutoApprove = true,
    AllowedTools = ["Read", "Glob", "Grep"]
};

var result = await client.RunTaskAsync(task);
Console.WriteLine($"Status: {result.Status}, Cost: ${result.CostUsd}");
```

### Scheduled Tasks

```csharp
var scheduledTasks = new List<ScheduledTask>
{
    new()
    {
        Task = new AutomationTask
        {
            Name = "Hourly Review",
            Query = "Review recent changes",
            AutoApprove = true,
            AllowedTools = ["Read", "Glob", "Grep"]
        },
        Interval = TimeSpan.FromHours(1)
    }
};

// Run scheduler loop
while (!cancellationToken.IsCancellationRequested)
{
    foreach (var st in scheduledTasks)
    {
        if (DateTime.UtcNow - st.LastRun >= st.Interval)
        {
            await client.RunTaskAsync(st.Task);
            st.LastRun = DateTime.UtcNow;
        }
    }
    await Task.Delay(TimeSpan.FromMinutes(1));
}
```

### Parallel Execution

```csharp
var tasks = new[]
{
    new AutomationTask { Name = "Task 1", Query = "Analyze structure", ... },
    new AutomationTask { Name = "Task 2", Query = "Check dependencies", ... },
    new AutomationTask { Name = "Task 3", Query = "Review security", ... }
};

var results = await Task.WhenAll(tasks.Select(t => client.RunTaskAsync(t)));

foreach (var result in results)
{
    Console.WriteLine($"{result.TaskName}: {result.Status}");
}
```

## Tool Safety

Claude Code has access to powerful tools. Use restrictions for safety:

| Risk Level | Tools | Recommendation |
|------------|-------|----------------|
| Low | Read, Glob, Grep, LS, WebSearch | Safe to auto-approve |
| Medium | WebFetch, Task | Review before approving |
| High | Bash, Write, Edit | Require manual approval |

```csharp
// Safe automation - only allow read operations
var options = new ClaudeOptions
{
    AutoApprove = true,
    AllowedTools = ["Read", "Glob", "Grep"]
};

// Or block dangerous tools
var options = new ClaudeOptions
{
    AutoApprove = true,
    DisallowedTools = ["Bash", "Write", "Edit"]
};
```

## Project Structure

```
claude-dotnet-automation/
├── HumanLayerAutomation/
│   ├── HumanLayerAutomation.csproj  # Project file
│   ├── ClaudeCodeClient.cs          # CLI wrapper client
│   ├── Models.cs                     # Data models
│   └── Program.cs                    # CLI and examples
├── docs/                             # Documentation
├── README.md                         # This file
└── dotnet-automation.sln             # Solution file
```

## API Reference

### ClaudeCodeClient

```csharp
// Constructor
var client = new ClaudeCodeClient(
    claudePath: "claude",           // Path to CLI
    defaultWorkingDir: ".",         // Working directory
    logger: logger                  // Optional ILogger
);

// Check if CLI is available
var version = await client.GetVersionAsync();

// Run a prompt
var result = await client.RunAsync(prompt, options);

// Run with streaming
var result = await client.RunStreamingAsync(prompt, onOutput, options);

// Run an automation task
var result = await client.RunTaskAsync(task);

// Continue a session
var result = await client.ContinueSessionAsync(sessionId, prompt, options);
```

### ClaudeOptions

```csharp
new ClaudeOptions
{
    WorkingDir = "/path/to/dir",
    Model = "sonnet",                    // opus, sonnet, haiku
    MaxTurns = 20,
    AutoApprove = false,
    OutputFormat = "json",               // text, json, stream-json
    AllowedTools = ["Read", "Glob"],
    DisallowedTools = ["Bash"],
    SystemPrompt = "Custom instructions",
    AppendSystemPrompt = "Additional instructions",
    ResumeSessionId = "session-id"
}
```

### ClaudeResult

```csharp
result.Success       // bool - exit code 0
result.ExitCode      // int
result.Output        // string - stdout
result.Error         // string - stderr
result.Duration      // TimeSpan
result.CostUsd       // decimal?
result.InputTokens   // int?
result.OutputTokens  // int?
result.SessionId     // string?
result.JsonResult    // ClaudeJsonResult? - parsed JSON output
```

## Migration from HumanLayer Daemon

If you were using the previous daemon-based approach:

| Old (daemon) | New (direct CLI) |
|--------------|------------------|
| `hld daemon start` | Not needed |
| `HumanLayerClient` | `ClaudeCodeClient` |
| `CreateSessionAsync` | `RunAsync` |
| `GetPendingApprovalsAsync` | Use `AutoApprove` or `AllowedTools` |
| SSE streaming | `RunStreamingAsync` |
| `HUMANLAYER_URL` | `CLAUDE_PATH` |

## Contributing

Contributions welcome!

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Submit a pull request

## License

MIT License - See [LICENSE](LICENSE) for details.

## Related Projects

- [Claude Code](https://claude.ai/code) - AI coding assistant CLI
- [Anthropic API](https://docs.anthropic.com) - Claude API documentation
