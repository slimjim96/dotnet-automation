# Examples: Building Applications with Claude Code Automation

This folder contains examples showing how to use the Claude Code .NET automation framework to build applications through automated prompts.

## Prerequisites

### Windows
```powershell
# Install .NET 10
winget install Microsoft.DotNet.SDK.10

# Install Claude Code CLI (native binary)
irm https://claude.ai/install.ps1 | iex

# Verify and authenticate (opens browser)
claude --version
claude
```

### Linux/macOS
```bash
# Install .NET 10
curl -sSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin --channel 10.0

# Install Claude Code CLI (native binary)
curl -fsSL https://claude.ai/install.sh | bash

# Verify and authenticate (opens browser)
claude --version
claude
```

---

## Example 1: Quick Demo - Verify Everything Works

```bash
cd HumanLayerAutomation
dotnet run -- demo
```

Expected output:
```
╔══════════════════════════════════════════════════════════════╗
║       Claude Code .NET Automation                            ║
╚══════════════════════════════════════════════════════════════╝

=== Demo Mode ===
1. Checking Claude CLI availability...
   Claude CLI: 1.0.x
2. Running a simple read-only task...
   Status: Success
   Duration: 5.2s
   Cost: $0.0012
```

---

## Example 2: Run a Single Automated Prompt

```bash
# Simple code analysis
dotnet run -- run "List all C# files in this project and briefly describe what each does"

# With environment variables
CLAUDE_MODEL=sonnet MAX_TURNS=20 dotnet run -- run "Review the code for potential improvements"
```

---

## Example 3: Building a Simple App (Step-by-Step)

This example shows how to use automated prompts to build a Todo CLI application.

### Step 1: Design the Application

```bash
dotnet run -- run "Design a simple command-line Todo app in C# with add, list, complete, and delete commands. Store data in JSON. Keep it minimal."
```

### Step 2: Generate the Code

```bash
# Generate the main program
dotnet run -- run "Generate a complete C# Program.cs for a Todo CLI. Commands: add, list, complete, delete. Use top-level statements. Store in ~/todos.json. Output only C# code."

# Generate the models
dotnet run -- run "Generate a C# TodoItem.cs with TodoItem record (Id, Title, IsCompleted, Priority, CreatedAt), Priority enum, and TodoStore class for JSON persistence. Output only C# code."
```

### Step 3: Generate Project Configuration

```bash
dotnet run -- run "Generate a minimal .csproj for a .NET 8 console app called TodoCli. No external packages. Output only XML."
```

### Step 4: Build and Test

```bash
cd output/TodoCli
dotnet build
dotnet run -- add "My first task" --priority high
dotnet run -- list
```

---

## Example 4: Build a Todo App with GitHub Copilot Only

These examples use **only** the GitHub Copilot Chat Completions API (no Claude, no OpenAI) to generate, build, and self-heal a Todo CLI app.

### Option A: PowerShell Script (Standalone)

Calls the Copilot API directly — no .NET project dependency needed:

```powershell
# Set your GitHub token (Pro subscription required for Copilot API)
$env:GITHUB_TOKEN = "ghp_your-token"
# Or use: $env:GITHUB_TOKEN = (gh auth token)

# Run the script
.\examples\build-todo-app-copilot.ps1

# Customize
.\examples\build-todo-app-copilot.ps1 -OutputDir ".\my-todo" -MaxRetries 5 -Model "gpt-4o"
```

What it does:
1. Checks prerequisites (`GITHUB_TOKEN`, .NET SDK, API connectivity)
2. Calls Copilot to design the app architecture
3. Generates `Program.cs`, `TodoItem.cs`, and `TodoCli.csproj` via API
4. Builds the project with **self-healing** — build errors are fed back to Copilot
5. Reports prompt count, token usage, and duration

### Option B: C# Programmatic (ICodeProvider Interface)

Uses the `GitHubCopilotProvider` class through the same `ICodeProvider` interface as Claude and OpenAI:

See [`CopilotTodoAppExample.cs.example`](CopilotTodoAppExample.cs.example) for the full implementation.

Key integration points:
```csharp
// Create Copilot provider directly (no MultiProvider fallback)
var copilot = new GitHubCopilotProvider(
    registry, quotaManager,
    new GitHubCopilotOptions { DefaultModel = "gpt-4o" },
    logger);

// Send a prompt through the standard interface
var result = await copilot.RunAsync(new ProviderRequest
{
    Prompt = "Generate a C# Program.cs for a Todo CLI...",
    SystemPrompt = "You are an expert C# developer...",
    Model = "gpt-4o",
    MaxTokens = 4096,
    AutoApprove = true
});

// Self-healing: feed build errors back to the same provider
if (buildFailed)
{
    var fixResult = await copilot.RunAsync(new ProviderRequest
    {
        Prompt = $"Fix these build errors:\n{buildOutput}\n\nCode:\n{currentCode}",
        SystemPrompt = codingSystemPrompt
    });
}
```

### Comparing Providers

| Feature | Claude (CLI) | Copilot (API) | OpenAI (API) |
|---------|-------------|---------------|-------------|
| Auth | `claude` CLI login | `GITHUB_TOKEN` env var | `OPENAI_API_KEY` env var |
| Coding | Full (tools + write) | Full (chat completions) | Full (chat completions) |
| Self-healing | ✅ via CLI re-prompt | ✅ via API re-prompt | ✅ via API re-prompt |
| Models | haiku, sonnet, opus | gpt-4o, gpt-4o-mini, o3-mini | gpt-4o |
| Free tier | Limited | 2000 completions/mo | Pay-per-use |
| Pro tier | Subscription | 1000 premium prompts/mo | Pay-per-use |

---

## Example 5: Parallel Analysis

Run multiple analysis tasks simultaneously for faster results:

```bash
dotnet run -- parallel
```

This runs 3 tasks concurrently:
1. File structure analysis
2. Dependency listing
3. Code summary

---

## Example 6: Scheduled Automation

Run recurring tasks on a schedule:

```bash
dotnet run -- scheduler
```

Default scheduled tasks:
- Code review summary (hourly)
- Security scan (every 4 hours)
- Documentation check (daily)

---

## Example 7: Streaming Output

Watch Claude's response in real-time:

```bash
dotnet run -- stream
```

---

## Creating Custom Automation Scripts

### Basic Pattern

```csharp
using HumanLayerAutomation;

// Create the client
using var client = new ClaudeCodeClient(
    defaultWorkingDir: "/path/to/project"
);

// Run a task with safety constraints
var result = await client.RunAsync(
    prompt: "Your prompt here",
    options: new ClaudeOptions
    {
        Model = "sonnet",           // haiku (cheap), sonnet (balanced), opus (powerful)
        MaxTurns = 20,              // Limit agent iterations
        AutoApprove = false,        // Require human approval for tool use
        AllowedTools = ["Read", "Glob", "Grep"],  // Restrict to safe tools
        OutputFormat = "json"       // Get structured output
    }
);

Console.WriteLine($"Success: {result.Success}");
Console.WriteLine($"Cost: ${result.CostUsd:F4}");
Console.WriteLine($"Output: {result.Output}");
```

### Multi-Step Build Pattern

```csharp
// Step 1: Design (read-only, safe)
var design = await client.RunAsync(
    "Design the architecture for a REST API",
    new ClaudeOptions { Model = "haiku", AutoApprove = true, AllowedTools = ["Read"] }
);

// Step 2: Generate code (may need approval)
var code = await client.RunAsync(
    "Generate the code based on this design: " + design.Output,
    new ClaudeOptions { Model = "sonnet", AutoApprove = false }
);

// Step 3: Write to disk (requires approval)
var write = await client.RunAsync(
    "Write the generated code to src/Api.cs",
    new ClaudeOptions { AutoApprove = false, AllowedTools = ["Write"] }
);
```

### Approval Flow

When `AutoApprove = false`, Claude will pause and ask for human approval before:
- Writing files
- Running bash commands
- Making external requests

This ensures human oversight for any potentially destructive operations.

---

## Tool Safety Levels

| Level | Tools | Auto-Approve? |
|-------|-------|---------------|
| **Safe** | Read, Glob, Grep, LS | ✅ Yes |
| **Medium** | WebFetch | ⚠️ With audit |
| **High Risk** | Bash, Write, Edit | ❌ Require approval |

---

## Cost Optimization Tips

1. **Use haiku for simple tasks** - 10x cheaper than sonnet
2. **Limit max_turns** - Fewer iterations = lower cost
3. **Restrict tools** - Fewer tools = simpler reasoning
4. **Use JSON output** - Get structured data, parse once
5. **Cache results** - Don't repeat identical queries

---

## Troubleshooting

### Claude CLI not found
```
Error: Cannot find Claude CLI
```
**Fix**: Install Claude Code:
- **Windows**: `irm https://claude.ai/install.ps1 | iex`
- **Linux/macOS**: `curl -fsSL https://claude.ai/install.sh | bash`

Then run `claude` to authenticate via browser.

### Permission denied
```
Error: Tool 'Write' requires approval
```
**Fix**: Either set `AutoApprove = true` (for trusted tasks) or handle the approval callback

### Timeout
```
Error: Task exceeded timeout
```
**Fix**: Increase `MaxTurns` or simplify the prompt

---

## Next Steps

- Review [../docs/patterns.md](../docs/patterns.md) for more automation patterns
- Check [../docs/security.md](../docs/security.md) for production deployment
- See [../docs/api-reference.md](../docs/api-reference.md) for full API documentation
