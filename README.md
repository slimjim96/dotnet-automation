# HumanLayer .NET Automation Client

Cross-platform .NET 10 client for automating AI agents with deterministic human oversight.

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

## What is This?

This is a .NET client library and automation framework for [HumanLayer](https://github.com/humanlayer/humanlayer) - a platform that provides human-in-the-loop capabilities for AI agents.

**Key Features:**
- Run AI agents (Claude Code) programmatically
- Queue approvals for periodic human review
- Schedule 24/7 background automation
- Support multiple AI providers via OpenRouter
- Real-time event streaming

```csharp
// Launch an AI task with human oversight
var client = new HumanLayerClient();

var result = await client.RunTaskAsync(new AutomationTask
{
    Name = "Code Review",
    Query = "Review this codebase for security issues",
    AutoApprove = false  // All tool uses require human approval
});
```

## Why Human Oversight?

AI agents are powerful but make mistakes. For high-stakes operations like:
- Executing shell commands
- Modifying production code
- Sending emails on your behalf

...you need **deterministic human oversight**, not probabilistic accuracy.

```
┌─────────────────────────────────────────────────────────────┐
│                    Stakes Spectrum                           │
│                                                              │
│   LOW              MEDIUM              HIGH                  │
│   ├────────────────┼────────────────────┤                   │
│   │                │                    │                    │
│   ▼                ▼                    ▼                    │
│ Auto-OK        Audit Trail        Human Approval             │
│ (Read, Grep)   (WebFetch)        (Bash, Write, Edit)        │
└─────────────────────────────────────────────────────────────┘
```

## Quick Start

### Prerequisites

1. **.NET 10 SDK**: https://dotnet.microsoft.com/download
2. **HumanLayer Daemon**: `npm install -g @anthropic/hld`
3. **Claude Code**: `npm install -g @anthropic/claude-code`

### Installation

```bash
# Clone the repository
git clone https://github.com/yourusername/humanlayer-dotnet.git
cd humanlayer-dotnet

# Build
dotnet build
```

### Run

```bash
# Start the HumanLayer daemon
hld daemon start

# Run the demo
dotnet run -- demo
```

## Automation Modes

### Demo Mode
Quick API demonstration:
```bash
dotnet run -- demo
```

### Batch Processor
Process pending approvals in batches with periodic human review:
```bash
dotnet run -- batch
```
- Auto-approves safe tools (Read, Glob, Grep)
- Prompts for manual review on dangerous operations
- Runs continuously with configurable intervals

### Task Scheduler
Run AI tasks on a schedule (cron-like):
```bash
dotnet run -- scheduler
```
- Hourly code reviews (auto-approved, read-only)
- Daily security scans (auto-approved with timeout)
- Weekly documentation updates (requires human approval)

### Event Monitor
Real-time event streaming via SSE:
```bash
dotnet run -- monitor
```

### Parallel Runner
Execute multiple AI tasks concurrently:
```bash
dotnet run -- parallel
```

## Configuration

### Environment Variables

| Variable | Description | Default |
|----------|-------------|---------|
| `HUMANLAYER_URL` | Daemon REST API URL | `http://localhost:7777/api/v1` |
| `WORKING_DIR` | Default working directory | Current directory |
| `OPENROUTER_API_KEY` | API key for OpenRouter | (none) |

### Using Alternative AI Models

Route requests through OpenRouter to use GPT-4, Llama, Mistral, and more:

```csharp
var task = new AutomationTask
{
    Query = "Analyze this codebase",
    ProxyBaseUrl = "https://openrouter.ai/api/v1",
    ProxyModel = "openai/gpt-4-turbo",
    ProxyApiKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY")
};
```

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    Your .NET Application                     │
│                                                              │
│   ┌──────────────────────────────────────────────────────┐  │
│   │                  HumanLayerClient                     │  │
│   │  • Session Management  • Approval Handling           │  │
│   │  • Event Streaming     • Task Orchestration          │  │
│   └──────────────────────────┬───────────────────────────┘  │
└──────────────────────────────┼──────────────────────────────┘
                               │ HTTP/REST
                               ▼
┌──────────────────────────────────────────────────────────────┐
│                   HumanLayer Daemon (hld)                    │
│                                                              │
│   Sessions ─────▶ Claude Code ─────▶ AI Work                │
│   Approvals ────▶ Human Review ────▶ Decisions              │
│                                                              │
└──────────────────────────────────────────────────────────────┘
```

## Documentation

| Document | Description |
|----------|-------------|
| [Getting Started](docs/getting-started.md) | Installation and setup guide |
| [Core Concepts](docs/concepts.md) | Terminology and principles |
| [Architecture](docs/architecture.md) | System design deep dive |
| [API Reference](docs/api-reference.md) | Complete endpoint documentation |
| [Automation Patterns](docs/patterns.md) | Best practices and examples |
| [Security](docs/security.md) | Security considerations |
| [Roadmap](docs/roadmap.md) | Future development plans |

## Code Examples

### Fully Automated (Safe Tasks)

```csharp
var task = new AutomationTask
{
    Name = "Code Analysis",
    Query = "Analyze code quality and identify issues",
    AutoApprove = true,
    AutoApproveTimeout = TimeSpan.FromMinutes(5),
    AllowedTools = new[] { "Read", "Glob", "Grep" }  // Safe tools only
};

var result = await client.RunTaskAsync(task);
```

### Human-in-the-Loop (Sensitive Tasks)

```csharp
// Launch task requiring approval
var session = await client.CreateSessionAsync(new CreateSessionRequest
{
    Query = "Deploy to production",
    DangerouslySkipPermissions = false
});

// Process approvals as they arrive
var pending = await client.GetPendingApprovalsAsync();
foreach (var approval in pending)
{
    if (IsSafe(approval))
        await client.ApproveAsync(approval.Id);
    else
        await client.DenyAsync(approval.Id, "Requires manual review");
}
```

### Scheduled Automation

```csharp
// Run every hour
scheduler.Schedule("Hourly Review", TimeSpan.FromHours(1), new AutomationTask
{
    Query = "Review recent code changes",
    AutoApprove = true,
    AllowedTools = new[] { "Read", "Glob", "Grep" }
});

// Run daily with human approval for changes
scheduler.Schedule("Daily Update", TimeSpan.FromHours(24), new AutomationTask
{
    Query = "Update documentation for new features",
    AutoApprove = false  // Requires approval
});

await scheduler.RunAsync(cancellationToken);
```

## Project Structure

```
humanlayer-dotnet/
├── HumanLayerAutomation.csproj  # Project file
├── HumanLayerClient.cs          # REST API client
├── Models.cs                     # Request/response models
├── Program.cs                    # CLI and examples
├── README.md                     # This file
└── docs/
    ├── getting-started.md        # Setup guide
    ├── concepts.md               # Core concepts
    ├── architecture.md           # System architecture
    ├── api-reference.md          # API documentation
    ├── patterns.md               # Best practices
    ├── security.md               # Security guide
    └── roadmap.md                # Future plans
```

## API Reference

### Session Methods

```csharp
// Launch new session
var session = await client.CreateSessionAsync(request);

// Get session details
var details = await client.GetSessionAsync(sessionId);

// List all sessions
var sessions = await client.ListSessionsAsync();

// Continue from existing session
var child = await client.ContinueSessionAsync(sessionId, "Next query");

// Wait for completion
var result = await client.WaitForSessionAsync(sessionId, timeout);
```

### Approval Methods

```csharp
// Get pending approvals
var pending = await client.GetPendingApprovalsAsync();

// Approve
await client.ApproveAsync(approvalId, "Looks good");

// Deny
await client.DenyAsync(approvalId, "Too risky");
```

### Event Streaming

```csharp
await client.SubscribeToEventsAsync(
    onEvent: evt => Console.WriteLine($"{evt.Type}: {evt.Data}"),
    eventTypes: new[] { "new_approval", "session_status_changed" }
);
```

## Contributing

Contributions are welcome! See [docs/roadmap.md](docs/roadmap.md) for contribution opportunities.

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Submit a pull request

## License

MIT License - See [LICENSE](LICENSE) for details.

## Related Projects

- [HumanLayer](https://github.com/humanlayer/humanlayer) - Core platform
- [Claude Code](https://claude.ai/code) - AI coding assistant
- [OpenRouter](https://openrouter.ai) - Multi-model gateway

## Support

- **Issues**: https://github.com/humanlayer/humanlayer/issues
- **Discussions**: https://github.com/humanlayer/humanlayer/discussions
- **Documentation**: https://humanlayer.dev/docs
