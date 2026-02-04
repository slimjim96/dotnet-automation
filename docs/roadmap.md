# Roadmap & Future Development

This document outlines planned features, potential enhancements, and contribution opportunities for the .NET HumanLayer automation client.

## Current Status

### ✅ Implemented (v0.1)

| Feature | Status | Notes |
|---------|--------|-------|
| REST API Client | ✅ Complete | Full session and approval management |
| Session Management | ✅ Complete | Create, list, get, continue, interrupt |
| Approval Handling | ✅ Complete | List, get, approve, deny |
| Event Streaming (SSE) | ✅ Complete | Real-time event subscription |
| Auto-Approve Modes | ✅ Complete | Full and timed auto-approve |
| OpenRouter Proxy | ✅ Complete | Alternative AI model support |
| Automation Patterns | ✅ Complete | Batch, scheduler, parallel execution |

---

## Short-Term Roadmap (Next Release)

### 🔄 In Progress

#### JSON-RPC Support
Direct Unix socket communication for lower latency:

```csharp
// Current: HTTP REST
var client = new HumanLayerClient("http://localhost:7777/api/v1");

// Future: Unix socket with JSON-RPC
var client = new HumanLayerClient(socketPath: "~/.humanlayer/daemon.sock");
```

#### Typed Approval Inputs
Strongly-typed tool input models:

```csharp
// Current: Dictionary<string, object>
approval.ToolInput["command"]

// Future: Typed models
if (approval is BashApproval bash)
{
    Console.WriteLine($"Command: {bash.Command}");
    Console.WriteLine($"Description: {bash.Description}");
}
```

### 📋 Planned

#### Configuration Builder
Fluent API for session configuration:

```csharp
var session = await client.CreateSession()
    .WithQuery("Refactor authentication")
    .InDirectory("/project")
    .UsingModel(Model.Sonnet)
    .WithMaxTurns(50)
    .AutoApproveFor(TimeSpan.FromMinutes(5))
    .AllowTools("Read", "Write", "Edit")
    .BlockTools("Bash")
    .ExecuteAsync();
```

#### Approval Policies
Declarative approval rules:

```csharp
var policy = ApprovalPolicy.Create()
    .AutoApprove(Tools.Read, Tools.Glob, Tools.Grep)
    .RequireApproval(Tools.Write, Tools.Edit)
    .Block(Tools.Bash)
    .WithTimeout(TimeSpan.FromMinutes(5));

client.SetDefaultPolicy(policy);
```

#### Retry and Resilience
Built-in retry logic with Polly:

```csharp
var client = new HumanLayerClient(options =>
{
    options.RetryCount = 3;
    options.RetryDelay = TimeSpan.FromSeconds(1);
    options.CircuitBreakerThreshold = 5;
});
```

---

## Medium-Term Roadmap

### Multi-Provider Orchestration
Run tasks across multiple AI providers:

```csharp
var orchestrator = new MultiProviderOrchestrator();

// Add providers
orchestrator.AddProvider("claude", new ClaudeProvider());
orchestrator.AddProvider("gpt4", new OpenRouterProvider("openai/gpt-4-turbo"));
orchestrator.AddProvider("llama", new OpenRouterProvider("meta-llama/llama-3-70b"));

// Route tasks to optimal provider
var result = await orchestrator.RunAsync(new AutomationTask
{
    Query = "Complex reasoning task",
    PreferredProvider = "claude",
    FallbackProviders = new[] { "gpt4", "llama" }
});
```

### Cost Tracking Dashboard
Built-in cost monitoring:

```csharp
var tracker = client.GetCostTracker();

Console.WriteLine($"Today: ${tracker.TodaySpend:F2}");
Console.WriteLine($"This week: ${tracker.WeekSpend:F2}");
Console.WriteLine($"This month: ${tracker.MonthSpend:F2}");

// Set alerts
tracker.SetBudget(daily: 10, weekly: 50, monthly: 200);
tracker.OnBudgetWarning += (sender, e) =>
{
    Console.WriteLine($"Warning: {e.PercentUsed}% of {e.Period} budget used");
};
```

### Session Templates
Reusable session configurations:

```csharp
// Define template
var codeReviewTemplate = new SessionTemplate
{
    Name = "Code Review",
    Query = "Review the recent changes in this repository",
    Model = "sonnet",
    AutoApprove = true,
    AllowedTools = new[] { "Read", "Glob", "Grep" }
};

// Save template
await client.SaveTemplateAsync(codeReviewTemplate);

// Use template
var session = await client.CreateFromTemplateAsync("Code Review",
    overrides: new { WorkingDir = "/my/project" });
```

### Webhook Integration
Push notifications for events:

```csharp
// Configure webhooks
await client.ConfigureWebhookAsync(new WebhookConfig
{
    Url = "https://your-server.com/humanlayer/events",
    Events = new[] { "new_approval", "session_completed" },
    Secret = "webhook-signing-secret"
});
```

---

## Long-Term Vision

### AI Agent SDK
Build custom AI agents with HumanLayer:

```csharp
public class CodeReviewAgent : HumanLayerAgent
{
    [Tool("review_file")]
    public async Task<string> ReviewFile(string path)
    {
        var content = await ReadFileAsync(path);
        var review = await AnalyzeAsync(content);

        if (review.HasCriticalIssues)
        {
            // Request human approval for critical findings
            await RequestApprovalAsync(
                $"Critical issues found in {path}",
                review.Issues
            );
        }

        return review.Summary;
    }
}

// Run agent
var agent = new CodeReviewAgent();
await agent.RunAsync("Review all TypeScript files");
```

### Distributed Execution
Scale across multiple machines:

```csharp
var cluster = new HumanLayerCluster();
cluster.AddNode("http://worker1:7777");
cluster.AddNode("http://worker2:7777");
cluster.AddNode("http://worker3:7777");

// Distribute tasks across cluster
var results = await cluster.MapAsync(
    tasks: myTasks,
    concurrency: 10
);
```

### Machine Learning Integration
Learn from approval patterns:

```csharp
// Train on historical approvals
var model = await client.TrainApprovalModelAsync();

// Predict approval likelihood
var prediction = model.Predict(approval);
Console.WriteLine($"Likely to approve: {prediction.Probability:P}");

// Auto-approve high-confidence predictions
if (prediction.Probability > 0.95)
{
    await client.ApproveAsync(approval.Id, "Auto-approved by ML model");
}
```

---

## Contribution Opportunities

### Good First Issues

| Issue | Description | Skills |
|-------|-------------|--------|
| Add XML documentation | Document all public APIs | C#, XML |
| Unit tests | Increase test coverage | C#, xUnit |
| Integration tests | Test against real daemon | C#, Docker |
| Example scripts | More automation examples | C# |

### Intermediate

| Issue | Description | Skills |
|-------|-------------|--------|
| JSON-RPC client | Unix socket communication | C#, Sockets |
| Polly integration | Resilience policies | C#, Polly |
| Logging providers | Structured logging | C#, Serilog |
| Configuration binding | IConfiguration support | C#, .NET |

### Advanced

| Issue | Description | Skills |
|-------|-------------|--------|
| Source generator | Typed tool inputs | C#, Roslyn |
| gRPC support | Alternative transport | C#, gRPC |
| Blazor dashboard | Web UI for monitoring | C#, Blazor |
| MAUI app | Cross-platform desktop UI | C#, MAUI |

---

## Feature Requests

Have an idea? We'd love to hear it!

1. **Check existing issues**: Search GitHub issues first
2. **Open a discussion**: For feature ideas, start a discussion
3. **Submit a proposal**: For larger features, create an RFC

### Request Template

```markdown
## Feature Request

### Problem
What problem does this solve?

### Proposed Solution
How should it work?

### Alternatives Considered
What else did you consider?

### Additional Context
Any other details?
```

---

## Versioning

This project follows [Semantic Versioning](https://semver.org/):

- **MAJOR**: Breaking API changes
- **MINOR**: New features, backward compatible
- **PATCH**: Bug fixes, backward compatible

### Release Schedule

| Version | Target | Focus |
|---------|--------|-------|
| v0.2 | Q2 2025 | JSON-RPC, Typed inputs |
| v0.3 | Q3 2025 | Configuration builder, Policies |
| v1.0 | Q4 2025 | Stable API, Production ready |

---

## Stay Updated

- **GitHub Releases**: Watch the repository for releases
- **Changelog**: See CHANGELOG.md for detailed changes
- **Blog**: https://humanlayer.dev/blog for announcements

---

## Acknowledgments

This project builds on the excellent work of:

- [HumanLayer](https://github.com/humanlayer/humanlayer) - Core platform
- [Claude Code](https://claude.ai/code) - AI agent runtime
- [OpenRouter](https://openrouter.ai) - Multi-model gateway
