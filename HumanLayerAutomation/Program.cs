using Microsoft.Extensions.Logging;
using HumanLayerAutomation;
using HumanLayerAutomation.Health;
using HumanLayerAutomation.Models;
using HumanLayerAutomation.Notifications;
using HumanLayerAutomation.Providers;

// Configure logging
using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Information);
});

var logger = loggerFactory.CreateLogger<Program>();

// Parse command line arguments
var mode = args.Length > 0 ? args[0] : "demo";

Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
Console.WriteLine("║       Claude Code .NET Automation                            ║");
Console.WriteLine("║       Direct CLI Integration (No Daemon Required)            ║");
Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
Console.WriteLine();

try
{
    switch (mode.ToLower())
    {
        case "demo":
            await RunDemoAsync(loggerFactory);
            break;
        case "run":
            await RunSingleTaskAsync(args, loggerFactory);
            break;
        case "scheduler":
            await RunSchedulerAsync(loggerFactory);
            break;
        case "parallel":
            await RunParallelTasksAsync(loggerFactory);
            break;
        case "stream":
            await RunStreamingDemoAsync(loggerFactory);
            break;
        case "build":
            await RunAutomatedBuildAsync(args, loggerFactory);
            break;
        case "build-todo":
            await RunBuildTodoAppAsync(loggerFactory);
            break;
        case "auto":
            await RunAutoBuilderAsync(args, loggerFactory);
            break;
        case "config":
            HandleConfigCommand(args);
            break;
        case "tasks":
            HandleTasksCommand(args);
            break;
        case "run-config":
            await RunFromConfigAsync(args, loggerFactory);
            break;
        case "models":
            HandleModelsCommand(args);
            break;
        case "providers":
            await HandleProvidersCommandAsync(args, loggerFactory);
            break;
        case "costs":
            HandleCostsCommand(args);
            break;
        case "quota":
            HandleQuotaCommand(args);
            break;
        case "notify":
            await HandleNotifyCommandAsync(args);
            break;
        case "health":
            await HandleHealthCommandAsync(args);
            break;
        case "status":
            await HandleStatusCommandAsync();
            break;
        case "setup":
            await HandleSetupCommandAsync();
            break;
        default:
            ShowUsage();
            break;
    }
}
catch (Exception ex)
{
    logger.LogError(ex, "Fatal error");
    Environment.Exit(1);
}

static void ShowUsage()
{
    Console.WriteLine("Usage: dotnet run -- <mode> [options]");
    Console.WriteLine();
    Console.WriteLine("Modes:");
    Console.WriteLine("  demo       - Quick demonstration of Claude CLI capabilities");
    Console.WriteLine("  run        - Run a single task: dotnet run -- run \"prompt\" [--approve] [--model name]");
    Console.WriteLine("  scheduler  - Scheduled task runner (cron-like)");
    Console.WriteLine("  parallel   - Run multiple AI tasks in parallel");
    Console.WriteLine("  stream     - Demo with real-time streaming output");
    Console.WriteLine("  build      - Build an app: dotnet run -- build \"app description\"");
    Console.WriteLine("  build-todo - Build a complete Todo CLI app (demo)");
    Console.WriteLine("  auto       - Autonomous builder with scenarios (see below)");
    Console.WriteLine();
    Console.WriteLine("Auto Builder Usage:");
    Console.WriteLine("  dotnet run -- auto [scenario] [strategy] [scope] \"description\" [target-path]");
    Console.WriteLine();
    Console.WriteLine("  Scenarios:  new (default), update, add");
    Console.WriteLine("  Strategies: first, efficient, thorough, balanced (default)");
    Console.WriteLine("  Scopes:     component, core, full (default)");
    Console.WriteLine();
    Console.WriteLine("  Examples:");
    Console.WriteLine("    dotnet run -- auto new \"Todo CLI app\" ./output/TodoApp");
    Console.WriteLine("    dotnet run -- auto add efficient \"Add delete command\" ./existing-project");
    Console.WriteLine("    dotnet run -- auto update first component \"Fix the save bug\" ./my-app");
    Console.WriteLine();
    Console.WriteLine("Config Commands:");
    Console.WriteLine("  config sample           - Output sample JSON config");
    Console.WriteLine("  config create <file>    - Create a config file interactively");
    Console.WriteLine("  run-config <file>       - Run a build from a JSON config file");
    Console.WriteLine("  tasks                   - List all tracked tasks");
    Console.WriteLine("  tasks status <id>       - Show status of a specific task");
    Console.WriteLine("  tasks clear             - Clear completed tasks from registry");
    Console.WriteLine();
    Console.WriteLine("Admin Commands:");
    Console.WriteLine("  models                  - List all registered models");
    Console.WriteLine("  models coding           - List models optimized for coding");
    Console.WriteLine("  models update <id>      - Update model pricing");
    Console.WriteLine("  providers               - Check provider availability");
    Console.WriteLine("  costs                   - Show usage costs summary");
    Console.WriteLine("  costs today             - Show today's costs");
    Console.WriteLine("  costs reset             - Reset usage tracking");
    Console.WriteLine("  quota                   - Show all provider quotas and metrics");
    Console.WriteLine("  quota <provider>        - Show quota for specific provider");
    Console.WriteLine("  quota set <provider>    - Configure provider quota settings");
    Console.WriteLine("  quota reset <provider>  - Reset quota window/month");
    Console.WriteLine();
    Console.WriteLine("Notification Commands:");
    Console.WriteLine("  notify                  - Show notification status");
    Console.WriteLine("  notify test             - Send test notifications");
    Console.WriteLine("  notify enable <channel> - Enable channel (email, discord, ntfy)");
    Console.WriteLine("  notify disable <channel>- Disable a channel");
    Console.WriteLine("  notify config           - Show notification configuration");
    Console.WriteLine();
    Console.WriteLine("Health & Status:");
    Console.WriteLine("  status                  - Quick system status (providers, quotas, notifications)");
    Console.WriteLine("  setup                   - Interactive setup wizard (cross-platform)");
    Console.WriteLine("  health                  - Full health check (all providers)");
    Console.WriteLine("  health claude           - Check Claude CLI status");
    Console.WriteLine("  health github           - Check GitHub CLI/Copilot status");
    Console.WriteLine("  health openai           - Check OpenAI API status");
    Console.WriteLine();
    Console.WriteLine("Environment Variables:");
    Console.WriteLine("  CLAUDE_PATH         - Path to claude executable (default: claude)");
    Console.WriteLine("  WORKING_DIR         - Default working directory");
    Console.WriteLine("  CLAUDE_MODEL        - Default model (opus, sonnet, haiku)");
    Console.WriteLine("  OUTPUT_DIR          - Output directory for build commands");
    Console.WriteLine();
    Console.WriteLine("Examples:");
    Console.WriteLine("  dotnet run -- demo");
    Console.WriteLine("  dotnet run -- run \"List files in current directory\"");
    Console.WriteLine("  dotnet run -- run \"Create a hello world app\" --approve");
    Console.WriteLine("  dotnet run -- run \"Refactor this code\" --approve --model opus");
    Console.WriteLine("  dotnet run -- build-todo");
    Console.WriteLine("  dotnet run -- build \"A calculator CLI with add, subtract, multiply, divide\"");
}

// ============================================================================
// Demo Mode - Quick CLI demonstration
// ============================================================================
static async Task RunDemoAsync(ILoggerFactory loggerFactory)
{
    Console.WriteLine("=== Demo Mode ===");
    Console.WriteLine("Demonstrating Claude CLI direct integration...\n");

    var claudePath = Environment.GetEnvironmentVariable("CLAUDE_PATH") ?? "claude";
    var workingDir = Environment.GetEnvironmentVariable("WORKING_DIR") ?? Environment.CurrentDirectory;

    Console.WriteLine($"Working directory: {Path.GetFullPath(workingDir)}");
    Console.WriteLine();

    using var client = new ClaudeCodeClient(
        claudePath: claudePath,
        defaultWorkingDir: workingDir,
        logger: loggerFactory.CreateLogger<ClaudeCodeClient>());

    // Check if Claude CLI is available
    Console.WriteLine("1. Checking Claude CLI availability...");
    try
    {
        var version = await client.GetVersionAsync();
        Console.WriteLine($"   Claude CLI: {version.Version}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"   ERROR: Cannot find Claude CLI: {ex.Message}");
        Console.WriteLine("   Install it with: npm install -g @anthropic/claude-code");
        return;
    }

    // Run a simple read-only task
    Console.WriteLine("\n2. Running a simple read-only task...");
    Console.WriteLine("   Prompt: \"List the files in the current directory briefly\"");

    var result = await client.RunAsync(
        prompt: "List the files in the current directory briefly. Just list the names, no details.",
        options: new ClaudeOptions
        {
            Model = "haiku", // Use cheapest model for demo
            MaxTurns = 5,
            AutoApprove = true, // Auto-approve for demo
            AllowedTools = ["Read", "Glob", "Bash"], // Restrict to safe tools
            OutputFormat = "json"
        });

    if (result.Success)
    {
        Console.WriteLine($"   Status: Success");
        Console.WriteLine($"   Duration: {result.Duration.TotalSeconds:F1}s");
        if (result.CostUsd.HasValue)
            Console.WriteLine($"   Cost: ${result.CostUsd:F4}");
        Console.WriteLine($"   Output: {Truncate(result.Output, 200)}");
    }
    else
    {
        Console.WriteLine($"   Status: Failed");
        Console.WriteLine($"   Error: {result.Error}");
    }

    // Example: Code analysis task
    Console.WriteLine("\n3. Code analysis example (dry run):");
    Console.WriteLine("   Would analyze: 'Review this codebase for potential issues'");
    Console.WriteLine("   To actually run: dotnet run -- run \"Review this codebase\"");

    Console.WriteLine("\n=== Demo Complete ===");
    Console.WriteLine("\nNext steps:");
    Console.WriteLine("  - Run a custom task: dotnet run -- run \"your prompt here\"");
    Console.WriteLine("  - Start scheduler: dotnet run -- scheduler");
    Console.WriteLine("  - Run parallel tasks: dotnet run -- parallel");
}

// ============================================================================
// Single Task Runner
// ============================================================================
static async Task RunSingleTaskAsync(string[] args, ILoggerFactory loggerFactory)
{
    if (args.Length < 2)
    {
        Console.WriteLine("Usage: dotnet run -- run \"your prompt\" [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --approve              Auto-approve all tool uses (Write, Edit, Bash)");
        Console.WriteLine("  --model <name>         Model to use: haiku, sonnet, opus (default: sonnet)");
        Console.WriteLine("  --max-turns <n>        Maximum agent turns (default: 20)");
        Console.WriteLine("  --dir <path>           Working directory for Claude (default: current dir)");
        Console.WriteLine();
        Console.WriteLine("Environment variable overrides:");
        Console.WriteLine("  AUTO_APPROVE=true      Same as --approve");
        Console.WriteLine("  CLAUDE_MODEL=sonnet    Same as --model");
        Console.WriteLine("  MAX_TURNS=20           Same as --max-turns");
        Console.WriteLine("  WORKING_DIR=./path     Same as --dir");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  dotnet run -- run \"List files in this directory\"");
        Console.WriteLine("  dotnet run -- run \"Create a hello world app\" --approve");
        Console.WriteLine("  dotnet run -- run \"Refactor this code\" --approve --model opus");
        Console.WriteLine("  dotnet run -- run \"Analyze code\" --dir ./my-project --model haiku");
        return;
    }

    // Parse CLI flags from args (skip "run" at args[0])
    var remainingArgs = args.Skip(1).ToList();
    string? prompt = null;
    var model = Environment.GetEnvironmentVariable("CLAUDE_MODEL") ?? "sonnet";
    var maxTurns = int.TryParse(Environment.GetEnvironmentVariable("MAX_TURNS"), out var mt) ? mt : 20;
    var autoApprove = Environment.GetEnvironmentVariable("AUTO_APPROVE")?.ToLower() == "true";
    var workingDir = Environment.GetEnvironmentVariable("WORKING_DIR") ?? Environment.CurrentDirectory;

    for (int i = 0; i < remainingArgs.Count; i++)
    {
        switch (remainingArgs[i].ToLower())
        {
            case "--approve":
            case "-a":
                autoApprove = true;
                break;
            case "--model":
            case "-m":
                if (i + 1 < remainingArgs.Count) model = remainingArgs[++i];
                break;
            case "--max-turns":
            case "-t":
                if (i + 1 < remainingArgs.Count && int.TryParse(remainingArgs[i + 1], out var turns))
                { maxTurns = turns; i++; }
                break;
            case "--dir":
            case "-d":
                if (i + 1 < remainingArgs.Count) workingDir = remainingArgs[++i];
                break;
            default:
                // First non-flag argument is the prompt
                if (prompt == null) prompt = remainingArgs[i];
                break;
        }
    }

    if (string.IsNullOrWhiteSpace(prompt))
    {
        Console.WriteLine("Error: No prompt provided.");
        Console.WriteLine("Usage: dotnet run -- run \"your prompt\" [--approve] [--model name]");
        return;
    }

    Console.WriteLine($"Running task with Claude ({model})...");
    Console.WriteLine($"Working dir:  {Path.GetFullPath(workingDir)}");
    Console.WriteLine($"Prompt:       {Truncate(prompt, 100)}");
    Console.WriteLine($"Auto-approve: {autoApprove}");
    Console.WriteLine($"Max turns:    {maxTurns}");
    Console.WriteLine();

    using var client = new ClaudeCodeClient(
        defaultWorkingDir: workingDir,
        logger: loggerFactory.CreateLogger<ClaudeCodeClient>());

    client.DefaultModel = model;

    var options = new ClaudeOptions
    {
        MaxTurns = maxTurns,
        AutoApprove = autoApprove,
        OutputFormat = "json"
    };

    var result = await client.RunAsync(prompt, options);

    // Detect permission-blocked responses and auto-retry with permissions enabled
    if (result.IsPermissionRequest && !autoApprove)
    {
        Console.WriteLine();
        Console.WriteLine("WARNING: Claude was blocked by tool permissions (Write/Edit/Bash not approved).");
        Console.WriteLine("         Retrying with AUTO_APPROVE=true (--dangerously-skip-permissions)...");
        Console.WriteLine();

        options = options with { AutoApprove = true };
        var retryResult = await client.RunAsync(prompt, options);

        // Use retry result, but accumulate cost/tokens from both attempts
        result = retryResult with
        {
            CostUsd = (result.CostUsd ?? 0) + (retryResult.CostUsd ?? 0),
            InputTokens = (result.InputTokens ?? 0) + (retryResult.InputTokens ?? 0),
            OutputTokens = (result.OutputTokens ?? 0) + (retryResult.OutputTokens ?? 0),
            Duration = result.Duration + retryResult.Duration
        };
    }

    Console.WriteLine($"\n=== Result ===");
    Console.WriteLine($"Status: {(result.Success ? "Success" : "Failed")}");
    Console.WriteLine($"Duration: {result.Duration.TotalSeconds:F1}s");

    if (result.CostUsd.HasValue)
        Console.WriteLine($"Cost: ${result.CostUsd:F4}");

    if (result.InputTokens.HasValue)
        Console.WriteLine($"Tokens: {result.InputTokens} in / {result.OutputTokens} out");

    if (result.IsQuestioningResponse)
    {
        Console.WriteLine();
        Console.WriteLine("NOTE: Claude returned a questioning/incomplete response instead of completing the task.");
        Console.WriteLine("      Try rephrasing with more specific instructions, or set AUTO_APPROVE=true.");
    }

    Console.WriteLine();
    Console.WriteLine("Output:");
    Console.WriteLine(result.Output);

    if (!string.IsNullOrEmpty(result.Error))
    {
        Console.WriteLine("\nErrors:");
        Console.WriteLine(result.Error);
    }
}

// ============================================================================
// Scheduler - Run tasks on a schedule
// ============================================================================
static async Task RunSchedulerAsync(ILoggerFactory loggerFactory)
{
    Console.WriteLine("=== Task Scheduler ===");
    Console.WriteLine("Running scheduled AI tasks.\n");

    var workingDir = Environment.GetEnvironmentVariable("WORKING_DIR") ?? Environment.CurrentDirectory;

    Console.WriteLine($"Working directory: {Path.GetFullPath(workingDir)}");
    Console.WriteLine();

    using var client = new ClaudeCodeClient(
        defaultWorkingDir: workingDir,
        logger: loggerFactory.CreateLogger<ClaudeCodeClient>());

    // Example scheduled tasks
    var scheduledTasks = new List<ScheduledTask>
    {
        new()
        {
            Task = new AutomationTask
            {
                Name = "Code Review Summary",
                Query = "Review any source code files in this directory and provide a brief summary of what the code does",
                WorkingDir = workingDir,
                Model = "haiku",
                MaxTurns = 10,
                AutoApprove = true,
                AllowedTools = ["Read", "Glob", "Grep"]
            },
            Interval = TimeSpan.FromHours(1)
        },
        new()
        {
            Task = new AutomationTask
            {
                Name = "Security Scan",
                Query = "Scan for potential security issues: hardcoded secrets, SQL injection, XSS vulnerabilities, or unsafe patterns",
                WorkingDir = workingDir,
                Model = "sonnet",
                MaxTurns = 20,
                AutoApprove = true,
                AllowedTools = ["Read", "Glob", "Grep"]
            },
            Interval = TimeSpan.FromHours(4)
        },
        new()
        {
            Task = new AutomationTask
            {
                Name = "Documentation Check",
                Query = "Check if code documentation is up to date and suggest any missing documentation",
                WorkingDir = workingDir,
                Model = "haiku",
                MaxTurns = 15,
                AutoApprove = true, // Read-only, safe
                AllowedTools = ["Read", "Glob", "Grep"]
            },
            Interval = TimeSpan.FromHours(24)
        }
    };

    var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
    };

    Console.WriteLine("Scheduled tasks:");
    foreach (var st in scheduledTasks)
    {
        Console.WriteLine($"  - {st.Task.Name}: Every {st.Interval.TotalHours}h");
    }
    Console.WriteLine("\nPress Ctrl+C to stop.\n");

    while (!cts.Token.IsCancellationRequested)
    {
        var now = DateTime.UtcNow;

        foreach (var st in scheduledTasks.Where(t => t.Enabled))
        {
            if (now - st.LastRun >= st.Interval)
            {
                Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm}] Running: {st.Task.Name}");

                try
                {
                    var result = await client.RunTaskAsync(st.Task, cts.Token);
                    st.LastRun = now;

                    Console.WriteLine($"  Status: {result.Status}");
                    Console.WriteLine($"  Duration: {result.Duration.TotalSeconds:F1}s");
                    if (result.CostUsd.HasValue)
                        Console.WriteLine($"  Cost: ${result.CostUsd:F4}");
                    if (!string.IsNullOrEmpty(result.Summary))
                        Console.WriteLine($"  Summary: {Truncate(result.Summary, 100)}");
                    Console.WriteLine();
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  Error: {ex.Message}");
                }
            }
        }

        try
        {
            await Task.Delay(TimeSpan.FromMinutes(1), cts.Token);
        }
        catch (OperationCanceledException)
        {
            break;
        }
    }

    Console.WriteLine("\nScheduler stopped.");
}

// ============================================================================
// Parallel - Run multiple tasks simultaneously
// ============================================================================
static async Task RunParallelTasksAsync(ILoggerFactory loggerFactory)
{
    Console.WriteLine("=== Parallel Task Runner ===");
    Console.WriteLine("Running multiple AI tasks concurrently.\n");

    var workingDir = Environment.GetEnvironmentVariable("WORKING_DIR") ?? Environment.CurrentDirectory;

    Console.WriteLine($"Working directory: {Path.GetFullPath(workingDir)}");
    Console.WriteLine();

    using var client = new ClaudeCodeClient(
        defaultWorkingDir: workingDir,
        logger: loggerFactory.CreateLogger<ClaudeCodeClient>());

    // Define parallel tasks
    var tasks = new List<AutomationTask>
    {
        new()
        {
            Name = "Task 1: File Structure",
            Query = "Briefly describe the file structure of this project",
            WorkingDir = workingDir,
            Model = "haiku",
            MaxTurns = 10,
            AutoApprove = true,
            AllowedTools = ["Glob", "Read"]
        },
        new()
        {
            Name = "Task 2: Dependencies",
            Query = "List the main dependencies of this project",
            WorkingDir = workingDir,
            Model = "haiku",
            MaxTurns = 10,
            AutoApprove = true,
            AllowedTools = ["Read", "Glob"]
        },
        new()
        {
            Name = "Task 3: Code Summary",
            Query = "Provide a one-paragraph summary of what this code does",
            WorkingDir = workingDir,
            Model = "haiku",
            MaxTurns = 10,
            AutoApprove = true,
            AllowedTools = ["Read", "Glob", "Grep"]
        }
    };

    Console.WriteLine($"Launching {tasks.Count} parallel tasks...\n");

    var startTime = DateTime.UtcNow;

    // Run all tasks in parallel
    var taskResults = await Task.WhenAll(
        tasks.Select(t => client.RunTaskAsync(t))
    );

    var totalDuration = DateTime.UtcNow - startTime;

    Console.WriteLine("\n=== Results ===\n");

    decimal totalCost = 0;
    foreach (var result in taskResults)
    {
        Console.WriteLine($"{result.TaskName}:");
        Console.WriteLine($"  Status: {result.Status}");
        Console.WriteLine($"  Duration: {result.Duration.TotalSeconds:F1}s");
        if (result.CostUsd.HasValue)
        {
            Console.WriteLine($"  Cost: ${result.CostUsd:F4}");
            totalCost += result.CostUsd.Value;
        }
        if (!string.IsNullOrEmpty(result.ErrorMessage))
            Console.WriteLine($"  Error: {result.ErrorMessage}");
        if (!string.IsNullOrEmpty(result.Summary))
            Console.WriteLine($"  Summary: {Truncate(result.Summary, 150)}");
        Console.WriteLine();
    }

    Console.WriteLine($"Total wall-clock time: {totalDuration.TotalSeconds:F1}s");
    Console.WriteLine($"Total cost: ${totalCost:F4}");
}

// ============================================================================
// Streaming Demo - Real-time output
// ============================================================================
static async Task RunStreamingDemoAsync(ILoggerFactory loggerFactory)
{
    Console.WriteLine("=== Streaming Demo ===");
    Console.WriteLine("Running a task with real-time output streaming.\n");

    var workingDir = Environment.GetEnvironmentVariable("WORKING_DIR") ?? Environment.CurrentDirectory;

    Console.WriteLine($"Working directory: {Path.GetFullPath(workingDir)}");
    Console.WriteLine();

    using var client = new ClaudeCodeClient(
        defaultWorkingDir: workingDir,
        logger: loggerFactory.CreateLogger<ClaudeCodeClient>());

    Console.WriteLine("Prompt: Explain what this project does in 2-3 sentences.\n");
    Console.WriteLine("--- Output (streaming) ---");

    var result = await client.RunStreamingAsync(
        prompt: "Explain what this project does in 2-3 sentences.",
        onOutput: chunk => Console.Write(chunk),
        options: new ClaudeOptions
        {
            Model = "haiku",
            MaxTurns = 5,
            AutoApprove = true,
            AllowedTools = ["Read", "Glob"]
        });

    Console.WriteLine("\n--- End Output ---\n");

    Console.WriteLine($"Status: {(result.Success ? "Success" : "Failed")}");
    Console.WriteLine($"Duration: {result.Duration.TotalSeconds:F1}s");
}

// ============================================================================
// Automated Build - Build apps with self-healing
// ============================================================================
static async Task RunAutomatedBuildAsync(string[] args, ILoggerFactory loggerFactory)
{
    if (args.Length < 2)
    {
        Console.WriteLine("Usage: dotnet run -- build \"app description\"");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  dotnet run -- build \"A calculator CLI with add, subtract, multiply, divide\"");
        Console.WriteLine("  dotnet run -- build \"A file organizer that sorts files by extension\"");
        return;
    }

    var description = args[1];
    var appName = ExtractAppName(description);
    var outputDir = Environment.GetEnvironmentVariable("OUTPUT_DIR")
        ?? Path.Combine(Environment.CurrentDirectory, "generated", appName);

    outputDir = Path.GetFullPath(outputDir);
    var workingDir = Environment.GetEnvironmentVariable("WORKING_DIR") ?? Environment.CurrentDirectory;

    Console.WriteLine($"Building app from description: {description}");
    Console.WriteLine($"Working directory:  {Path.GetFullPath(workingDir)}");
    Console.WriteLine($"Output directory:   {outputDir}");
    Console.WriteLine();

    using var client = new ClaudeCodeClient(
        defaultWorkingDir: workingDir,
        logger: loggerFactory.CreateLogger<ClaudeCodeClient>());

    var builder = new AutomatedAppBuilder(
        client,
        new BuilderOptions
        {
            Model = Environment.GetEnvironmentVariable("CLAUDE_MODEL") ?? "sonnet",
            MaxRetries = 3,
            MaxBuildFixAttempts = 3
        },
        loggerFactory.CreateLogger<AutomatedAppBuilder>());

    // Infer files needed from description
    var spec = new AppSpec
    {
        Name = appName,
        Description = description,
        OutputDirectory = outputDir,
        Framework = "net8.0",
        Requirements = ["Simple console application", "No external NuGet packages", "Clean, user-friendly output"],
        Files =
        [
            new FileSpec
            {
                FileName = "Program.cs",
                FileType = "csharp",
                Purpose = "Main entry point with command handling",
                Requirements = ["Top-level statements", "Manual argument parsing", "Error handling"]
            }
        ],
        TestCommands = ["--help"]
    };

    var result = await builder.BuildAsync(spec);

    if (result.Success)
    {
        Console.WriteLine("To run your app:");
        Console.WriteLine($"  cd {outputDir}");
        Console.WriteLine($"  dotnet run");
    }
}

static async Task RunBuildTodoAppAsync(ILoggerFactory loggerFactory)
{
    Console.WriteLine("=== Build Todo CLI App (Self-Healing Demo) ===");
    Console.WriteLine();

    var outputDir = Path.GetFullPath(Environment.GetEnvironmentVariable("OUTPUT_DIR")
        ?? Path.Combine(Environment.CurrentDirectory, "generated", "TodoApp"));

    var workingDir = Environment.GetEnvironmentVariable("WORKING_DIR") ?? Environment.CurrentDirectory;

    Console.WriteLine($"Working directory: {Path.GetFullPath(workingDir)}");
    Console.WriteLine($"Output directory:  {outputDir}");
    Console.WriteLine();

    using var client = new ClaudeCodeClient(
        defaultWorkingDir: workingDir,
        logger: loggerFactory.CreateLogger<ClaudeCodeClient>());

    var builder = new AutomatedAppBuilder(
        client,
        new BuilderOptions
        {
            Model = Environment.GetEnvironmentVariable("CLAUDE_MODEL") ?? "sonnet",
            MaxRetries = 3,
            MaxBuildFixAttempts = 3
        },
        loggerFactory.CreateLogger<AutomatedAppBuilder>());

    var spec = new AppSpec
    {
        Name = "TodoApp",
        Description = "A command-line Todo application with task management",
        OutputDirectory = outputDir,
        Framework = "net8.0",
        Requirements =
        [
            "Commands: add, list, complete, delete",
            "Store todos in JSON file (todos.json)",
            "Support priority levels: high, medium, low",
            "Display task status (pending/completed)",
            "No external NuGet packages"
        ],
        Files =
        [
            new FileSpec
            {
                FileName = "TodoItem.cs",
                FileType = "csharp",
                Purpose = "Data models and FULLY IMPLEMENTED persistence layer",
                Requirements =
                [
                    "Priority enum (Low=0, Medium=1, High=2)",
                    "TodoItem record with: int Id, string Title, bool IsCompleted, Priority Priority, DateTime CreatedAt",
                    "TodoStore class with COMPLETE implementations for: Load(reads from todos.json), Save(writes to todos.json), Add, GetAll, GetPending, GetCompleted, Complete(by id), Delete(by id)",
                    "Use System.Text.Json for serialization with WriteIndented=true",
                    "Handle file not exists case in Load (return empty list)",
                    "All methods must be FULLY IMPLEMENTED, not stubs"
                ]
            },
            new FileSpec
            {
                FileName = "Program.cs",
                FileType = "csharp",
                Purpose = "CLI entry point that USES TodoStore for all operations",
                Requirements =
                [
                    "Top-level statements - no namespace",
                    "Create TodoStore instance and use it for ALL operations",
                    "add command: parse args, call store.Add(), show confirmation",
                    "list command: call store.GetAll/GetPending/GetCompleted, display formatted list with IDs",
                    "complete command: parse id, call store.Complete(id), show result",
                    "delete command: parse id, call store.Delete(id), show result",
                    "Show task ID, priority indicator (!!!/!!/!), completion status [x] or [ ]",
                    "MUST actually call TodoStore methods, not just print fake messages"
                ]
            }
        ],
        TestCommands =
        [
            "",
            "add \"Test task\" --priority high",
            "list --all"
        ]
    };

    var result = await builder.BuildAsync(spec);

    if (result.Success)
    {
        Console.WriteLine("Your Todo app is ready! Try these commands:");
        Console.WriteLine($"  cd {outputDir}");
        Console.WriteLine("  dotnet run -- add \"Buy groceries\" --priority high");
        Console.WriteLine("  dotnet run -- list");
        Console.WriteLine("  dotnet run -- complete 1");
    }
}

static string ExtractAppName(string description)
{
    // Try to extract a reasonable app name from the description
    var words = description.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    // Look for common patterns
    for (int i = 0; i < words.Length - 1; i++)
    {
        if (words[i].Equals("a", StringComparison.OrdinalIgnoreCase) ||
            words[i].Equals("an", StringComparison.OrdinalIgnoreCase))
        {
            var nextWord = words[i + 1].Trim(',', '.', '!', '?');
            if (nextWord.Length > 2)
            {
                return char.ToUpper(nextWord[0]) + nextWord[1..].ToLower() + "App";
            }
        }
    }

    // Fallback: use first significant word
    foreach (var word in words)
    {
        var clean = word.Trim(',', '.', '!', '?', '"', '\'');
        if (clean.Length > 3 && !IsCommonWord(clean))
        {
            return char.ToUpper(clean[0]) + clean[1..].ToLower() + "App";
        }
    }

    return "GeneratedApp";
}

static bool IsCommonWord(string word) =>
    word.ToLower() is "the" or "with" or "that" or "this" or "from" or "have" or "create" or "make" or "build";

// ============================================================================
// Admin Commands - Model and Provider Management
// ============================================================================
static void HandleModelsCommand(string[] args)
{
    var registry = new ModelRegistry();
    var subCommand = args.Length > 1 ? args[1].ToLower() : "list";

    switch (subCommand)
    {
        case "list":
            var models = registry.GetAllModels();
            Console.WriteLine("Registered Models:");
            Console.WriteLine("─".PadRight(100, '─'));
            Console.WriteLine($"{"ID",-35} {"Provider",-12} {"Coding",-8} {"In/1M",-10} {"Out/1M",-10} {"Enabled"}");
            Console.WriteLine("─".PadRight(100, '─'));

            foreach (var m in models.OrderByDescending(x => x.CodingPriority))
            {
                var enabled = m.Enabled ? "Yes" : "No";
                Console.WriteLine($"{m.Id,-35} {m.Provider,-12} {m.CodingPriority,-8} ${m.CostPerMillionInput,-9:F2} ${m.CostPerMillionOutput,-9:F2} {enabled}");
            }
            break;

        case "coding":
            var codingModels = registry.GetCodingModels();
            Console.WriteLine("Models Optimized for Coding (by priority):");
            Console.WriteLine("─".PadRight(90, '─'));

            foreach (var cm in codingModels)
            {
                Console.WriteLine($"  [{cm.CodingPriority,3}] {cm.DisplayName,-25} ({cm.Provider})");
                Console.WriteLine($"        Cost: ${cm.CostPerMillionInput}/M in, ${cm.CostPerMillionOutput}/M out");
                if (!string.IsNullOrEmpty(cm.Notes))
                    Console.WriteLine($"        {cm.Notes}");
                Console.WriteLine();
            }
            break;

        case "update":
            if (args.Length < 3)
            {
                Console.WriteLine("Usage: models update <model-id>");
                return;
            }
            var modelId = args[2];
            var model = registry.GetModel(modelId) ?? registry.GetModelByAlias(modelId);
            if (model == null)
            {
                Console.WriteLine($"Model not found: {modelId}");
                return;
            }

            Console.WriteLine($"Updating: {model.DisplayName} ({model.Id})");
            Console.WriteLine($"Current pricing: ${model.CostPerMillionInput}/M input, ${model.CostPerMillionOutput}/M output");

            Console.Write("New input cost per million (or Enter to skip): ");
            var inputStr = Console.ReadLine();
            Console.Write("New output cost per million (or Enter to skip): ");
            var outputStr = Console.ReadLine();

            if (decimal.TryParse(inputStr, out var newInput) || decimal.TryParse(outputStr, out var newOutput))
            {
                registry.UpdatePricing(
                    model.Id,
                    decimal.TryParse(inputStr, out newInput) ? newInput : model.CostPerMillionInput,
                    decimal.TryParse(outputStr, out newOutput) ? newOutput : model.CostPerMillionOutput);
                Console.WriteLine("Pricing updated.");
            }
            break;

        case "enable":
        case "disable":
            if (args.Length < 3)
            {
                Console.WriteLine($"Usage: models {subCommand} <model-id>");
                return;
            }
            var targetId = args[2];
            registry.SetModelEnabled(targetId, subCommand == "enable");
            Console.WriteLine($"Model {targetId} {subCommand}d.");
            break;

        case "add":
            Console.WriteLine("Adding new model...");
            Console.Write("Model ID: ");
            var newId = Console.ReadLine() ?? "";
            Console.Write("Provider (anthropic/github/openai): ");
            var provider = Console.ReadLine() ?? "";
            Console.Write("Display name: ");
            var displayName = Console.ReadLine() ?? "";
            Console.Write("Aliases (comma-separated): ");
            var aliasesStr = Console.ReadLine() ?? "";
            Console.Write("Input cost per million tokens: ");
            var inputCost = decimal.TryParse(Console.ReadLine(), out var ic) ? ic : 0;
            Console.Write("Output cost per million tokens: ");
            var outputCost = decimal.TryParse(Console.ReadLine(), out var oc) ? oc : 0;
            Console.Write("Coding priority (0-100): ");
            var priority = int.TryParse(Console.ReadLine(), out var p) ? p : 50;

            registry.UpsertModel(new ModelInfo
            {
                Id = newId,
                Provider = provider,
                DisplayName = displayName,
                Aliases = aliasesStr.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(a => a.Trim()).ToList(),
                CostPerMillionInput = inputCost,
                CostPerMillionOutput = outputCost,
                CodingPriority = priority,
                Capabilities = ["coding"]
            });
            Console.WriteLine("Model added.");
            break;

        default:
            Console.WriteLine("Model Commands:");
            Console.WriteLine("  models              - List all models");
            Console.WriteLine("  models coding       - List coding-optimized models");
            Console.WriteLine("  models update <id>  - Update model pricing");
            Console.WriteLine("  models enable <id>  - Enable a model");
            Console.WriteLine("  models disable <id> - Disable a model");
            Console.WriteLine("  models add          - Add a new model");
            break;
    }
}

static async Task HandleProvidersCommandAsync(string[] args, ILoggerFactory loggerFactory)
{
    var registry = new ModelRegistry();
    var quotaManager = new QuotaManager();

    using var claudeClient = new ClaudeCodeClient(logger: loggerFactory.CreateLogger<ClaudeCodeClient>());

    var multiProvider = MultiProvider.CreateDefault(claudeClient, registry, quotaManager, loggerFactory);

    Console.WriteLine("Checking provider availability...");
    Console.WriteLine();

    var status = await multiProvider.GetProviderStatusAsync();

    Console.WriteLine("Provider Status:");
    Console.WriteLine("─".PadRight(50, '─'));

    foreach (var (providerId, available) in status)
    {
        var statusStr = available ? "✓ Available" : "✗ Not available";
        var color = available ? ConsoleColor.Green : ConsoleColor.Red;

        Console.Write($"  {providerId,-15} ");
        var prevColor = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine(statusStr);
        Console.ForegroundColor = prevColor;
    }

    Console.WriteLine();

    // Show quota info
    var quota = await multiProvider.GetQuotaAsync();
    if (quota != null)
    {
        Console.WriteLine("Quota:");
        if (quota.IsUnlimited)
            Console.WriteLine("  Unlimited (subscription-based or no limit set)");
        else if (quota.RemainingBudget.HasValue)
            Console.WriteLine($"  Remaining budget: ${quota.RemainingBudget:F2}");
    }
}

static void HandleCostsCommand(string[] args)
{
    var registry = new ModelRegistry();
    var subCommand = args.Length > 1 ? args[1].ToLower() : "summary";

    switch (subCommand)
    {
        case "summary":
        case "all":
            var allSummary = registry.GetUsageSummary();
            PrintCostSummary("All Time", allSummary, registry);
            break;

        case "today":
            var todaySummary = registry.GetUsageSummary(DateTime.Today);
            PrintCostSummary("Today", todaySummary, registry);
            break;

        case "week":
            var weekSummary = registry.GetUsageSummary(DateTime.Today.AddDays(-7));
            PrintCostSummary("Last 7 Days", weekSummary, registry);
            break;

        case "month":
            var monthSummary = registry.GetUsageSummary(DateTime.Today.AddDays(-30));
            PrintCostSummary("Last 30 Days", monthSummary, registry);
            break;

        case "reset":
            Console.Write("Are you sure you want to reset usage tracking? (y/N): ");
            if (Console.ReadLine()?.ToLower() == "y")
            {
                // Recreate registry to clear usage
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var path = Path.Combine(appData, "dotnet-automation", "model-registry.json");
                if (File.Exists(path))
                {
                    File.Delete(path);
                    Console.WriteLine("Usage tracking reset.");
                }
            }
            break;

        default:
            Console.WriteLine("Cost Commands:");
            Console.WriteLine("  costs              - Show all-time summary");
            Console.WriteLine("  costs today        - Show today's costs");
            Console.WriteLine("  costs week         - Show last 7 days");
            Console.WriteLine("  costs month        - Show last 30 days");
            Console.WriteLine("  costs reset        - Reset usage tracking");
            break;
    }
}

static void HandleQuotaCommand(string[] args)
{
    var quotaManager = new QuotaManager();
    var subCommand = args.Length > 1 ? args[1].ToLower() : "list";

    // Check if it's a provider name
    var providerQuota = quotaManager.GetProviderQuota(subCommand);
    if (providerQuota != null)
    {
        // Show specific provider
        PrintProviderQuota(providerQuota, quotaManager);
        return;
    }

    switch (subCommand)
    {
        case "list":
        case "all":
            var metrics = quotaManager.GetAllMetrics();
            Console.WriteLine("Provider Quotas:");
            Console.WriteLine("─".PadRight(80, '─'));

            foreach (var m in metrics)
            {
                var quota = quotaManager.GetProviderQuota(m.ProviderId);
                if (quota == null) continue;

                Console.WriteLine($"\n{quota.DisplayName} ({quota.ProviderId}) - {quota.BillingModel}");

                switch (quota.BillingModel)
                {
                    case BillingModel.TimeBasedReset:
                        var timeLeft = quotaManager.GetTimeUntilReset(m.ProviderId);
                        var pct = quota.TokensPerWindow > 0 ? (m.CurrentWindowUsage * 100.0 / quota.TokensPerWindow) : 0;
                        Console.WriteLine($"  Window: {m.CurrentWindowUsage:N0} / {m.WindowLimit:N0} tokens ({pct:F1}%)");
                        Console.WriteLine($"  Resets in: {timeLeft:hh\\:mm\\:ss}");
                        break;

                    case BillingModel.MonthlyLimit:
                        var monthPct = quota.MonthlyLimit > 0 ? (m.CurrentMonthUsage * 100.0 / quota.MonthlyLimit) : 0;
                        Console.WriteLine($"  Month: {m.CurrentMonthUsage} / {m.MonthlyLimit} prompts ({monthPct:F1}%)");
                        Console.WriteLine($"  Daily budget: {m.DailyBudget} | Used today: {m.UsedToday}");
                        Console.WriteLine($"  Days remaining: {m.DaysRemaining}");
                        break;

                    case BillingModel.PayPerUse:
                        Console.WriteLine($"  Spent: ${m.CurrentMonthSpend:F2} / ${m.BudgetLimit:F2} budget");
                        break;
                }

                Console.WriteLine($"  Enabled: {(quota.Enabled ? "Yes" : "No")}");
            }
            break;

        case "set":
            if (args.Length < 3)
            {
                Console.WriteLine("Usage: quota set <provider>");
                return;
            }
            ConfigureProviderQuota(args[2], quotaManager);
            break;

        case "reset":
            if (args.Length < 3)
            {
                Console.WriteLine("Usage: quota reset <provider>");
                return;
            }
            var providerId = args[2].ToLower();
            var pq = quotaManager.GetProviderQuota(providerId);
            if (pq == null)
            {
                Console.WriteLine($"Provider not found: {providerId}");
                return;
            }

            if (pq.BillingModel == BillingModel.TimeBasedReset)
            {
                quotaManager.ResetWindow(providerId);
                Console.WriteLine($"Reset window for {pq.DisplayName}");
            }
            else if (pq.BillingModel == BillingModel.MonthlyLimit)
            {
                quotaManager.ResetMonthlyUsage(providerId);
                Console.WriteLine($"Reset monthly usage for {pq.DisplayName}");
            }
            break;

        default:
            Console.WriteLine("Quota Commands:");
            Console.WriteLine("  quota                - List all provider quotas");
            Console.WriteLine("  quota <provider>     - Show specific provider quota");
            Console.WriteLine("  quota set <provider> - Configure quota settings");
            Console.WriteLine("  quota reset <provider> - Reset quota counter");
            break;
    }
}

static void PrintProviderQuota(ProviderQuota quota, QuotaManager quotaManager)
{
    var metrics = quotaManager.GetMetrics(quota.ProviderId);

    Console.WriteLine($"{quota.DisplayName}");
    Console.WriteLine("─".PadRight(50, '─'));
    Console.WriteLine($"Provider ID:    {quota.ProviderId}");
    Console.WriteLine($"Billing Model:  {quota.BillingModel}");
    Console.WriteLine($"Enabled:        {(quota.Enabled ? "Yes" : "No")}");

    if (!string.IsNullOrEmpty(quota.Notes))
        Console.WriteLine($"Notes:          {quota.Notes}");

    Console.WriteLine();

    switch (quota.BillingModel)
    {
        case BillingModel.TimeBasedReset:
            var timeLeft = quotaManager.GetTimeUntilReset(quota.ProviderId);
            Console.WriteLine("Time-Based Quota:");
            Console.WriteLine($"  Reset interval: {quota.ResetIntervalHours} hours");
            Console.WriteLine($"  Tokens per window: {quota.TokensPerWindow:N0}");
            Console.WriteLine($"  Current usage: {metrics.CurrentWindowUsage:N0}");
            Console.WriteLine($"  Remaining: {metrics.WindowLimit - metrics.CurrentWindowUsage:N0}");
            Console.WriteLine($"  Window started: {quota.CurrentWindowStart:yyyy-MM-dd HH:mm}");
            Console.WriteLine($"  Resets in: {timeLeft:hh\\:mm\\:ss}");
            break;

        case BillingModel.MonthlyLimit:
            Console.WriteLine("Monthly Limit:");
            Console.WriteLine($"  Monthly limit: {quota.MonthlyLimit} premium prompts");
            Console.WriteLine($"  Used this month: {metrics.CurrentMonthUsage}");
            Console.WriteLine($"  Remaining: {metrics.MonthlyLimit - metrics.CurrentMonthUsage}");
            Console.WriteLine($"  Daily budget: {metrics.DailyBudget}");
            Console.WriteLine($"  Used today: {metrics.UsedToday}");
            Console.WriteLine($"  Pacing multiplier: {quota.PacingMultiplier:F1}x");
            Console.WriteLine($"  Enforce pacing: {(quota.EnforcePacing ? "Yes" : "No")}");
            break;

        case BillingModel.PayPerUse:
            Console.WriteLine("Pay Per Use:");
            Console.WriteLine($"  Monthly budget: ${quota.MonthlyBudgetUsd:F2}");
            Console.WriteLine($"  Spent this month: ${metrics.CurrentMonthSpend:F2}");
            Console.WriteLine($"  Remaining: ${metrics.BudgetLimit - metrics.CurrentMonthSpend:F2}");
            break;
    }
}

static void ConfigureProviderQuota(string providerId, QuotaManager quotaManager)
{
    var quota = quotaManager.GetProviderQuota(providerId);
    if (quota == null)
    {
        Console.WriteLine($"Provider not found: {providerId}");
        Console.WriteLine("Available providers: anthropic, github, openai");
        return;
    }

    Console.WriteLine($"Configuring {quota.DisplayName}...");
    Console.WriteLine($"Current billing model: {quota.BillingModel}");
    Console.WriteLine();

    switch (quota.BillingModel)
    {
        case BillingModel.TimeBasedReset:
            Console.Write($"Reset interval hours [{quota.ResetIntervalHours}]: ");
            if (int.TryParse(Console.ReadLine(), out var hours) && hours > 0)
                quota.ResetIntervalHours = hours;

            Console.Write($"Tokens per window [{quota.TokensPerWindow}]: ");
            if (int.TryParse(Console.ReadLine(), out var tokens) && tokens > 0)
                quota.TokensPerWindow = tokens;
            break;

        case BillingModel.MonthlyLimit:
            Console.Write($"Monthly limit [{quota.MonthlyLimit}]: ");
            if (int.TryParse(Console.ReadLine(), out var limit) && limit > 0)
                quota.MonthlyLimit = limit;

            Console.Write($"Pacing multiplier [{quota.PacingMultiplier:F1}]: ");
            if (decimal.TryParse(Console.ReadLine(), out var pacing) && pacing > 0)
                quota.PacingMultiplier = pacing;

            Console.Write($"Enforce pacing (y/n) [{(quota.EnforcePacing ? "y" : "n")}]: ");
            var enforce = Console.ReadLine()?.ToLower();
            if (enforce == "y") quota.EnforcePacing = true;
            else if (enforce == "n") quota.EnforcePacing = false;
            break;

        case BillingModel.PayPerUse:
            Console.Write($"Monthly budget USD [{quota.MonthlyBudgetUsd:F2}]: ");
            if (decimal.TryParse(Console.ReadLine(), out var budget) && budget >= 0)
                quota.MonthlyBudgetUsd = budget;
            break;
    }

    Console.Write($"Enabled (y/n) [{(quota.Enabled ? "y" : "n")}]: ");
    var enabled = Console.ReadLine()?.ToLower();
    if (enabled == "y") quota.Enabled = true;
    else if (enabled == "n") quota.Enabled = false;

    quotaManager.UpdateProviderQuota(quota);
    Console.WriteLine("Quota configuration saved.");
}

static void PrintCostSummary(string period, Dictionary<string, ModelUsageSummary> summary, ModelRegistry registry)
{
    Console.WriteLine($"Usage Summary - {period}:");
    Console.WriteLine("─".PadRight(80, '─'));

    if (summary.Count == 0)
    {
        Console.WriteLine("  No usage recorded.");
        return;
    }

    Console.WriteLine($"{"Model",-35} {"Requests",-10} {"Input Tokens",-15} {"Output Tokens",-15} {"Cost"}");
    Console.WriteLine("─".PadRight(80, '─'));

    decimal totalCost = 0;
    int totalRequests = 0;

    foreach (var (modelId, usage) in summary.OrderByDescending(s => s.Value.TotalCost))
    {
        var model = registry.GetModel(modelId);
        var displayName = model?.DisplayName ?? modelId;
        if (displayName.Length > 33) displayName = displayName[..30] + "...";

        Console.WriteLine($"{displayName,-35} {usage.RequestCount,-10} {usage.TotalInputTokens,-15:N0} {usage.TotalOutputTokens,-15:N0} ${usage.TotalCost:F4}");

        totalCost += usage.TotalCost;
        totalRequests += usage.RequestCount;
    }

    Console.WriteLine("─".PadRight(80, '─'));
    Console.WriteLine($"{"TOTAL",-35} {totalRequests,-10} {"",-15} {"",-15} ${totalCost:F4}");
    Console.WriteLine();
}

// ============================================================================
// Status Command - Consolidated view
// ============================================================================
static async Task HandleStatusCommandAsync()
{
    var status = new SystemStatus();
    await status.PrintCompactStatusAsync();
}

// ============================================================================
// Setup Wizard
// ============================================================================
static async Task HandleSetupCommandAsync()
{
    var wizard = new SetupWizard();
    await wizard.RunAsync();
}

// ============================================================================
// Health Check Commands
// ============================================================================
static async Task HandleHealthCommandAsync(string[] args)
{
    var healthService = new HealthCheckService();
    var subCommand = args.Length > 1 ? args[1].ToLower() : "all";

    switch (subCommand)
    {
        case "all":
        case "full":
            Console.WriteLine("Running full health check...");
            Console.WriteLine();
            var report = await healthService.CheckAllAsync();
            report.PrintConsole();
            break;

        case "claude":
        case "anthropic":
            Console.WriteLine("Checking Claude CLI...");
            var claudeHealth = await healthService.CheckClaudeAsync();
            PrintSingleProviderHealth(claudeHealth);
            break;

        case "github":
        case "gh":
        case "copilot":
            Console.WriteLine("Checking GitHub CLI...");
            var ghHealth = await healthService.CheckGitHubAsync();
            PrintSingleProviderHealth(ghHealth);
            break;

        case "openai":
        case "gpt":
            Console.WriteLine("Checking OpenAI API...");
            var openaiHealth = await healthService.CheckOpenAIAsync();
            PrintSingleProviderHealth(openaiHealth);
            break;

        default:
            Console.WriteLine("Health Check Commands:");
            Console.WriteLine("  health              - Full health check (all providers)");
            Console.WriteLine("  health claude       - Check Claude CLI status");
            Console.WriteLine("  health github       - Check GitHub CLI/Copilot status");
            Console.WriteLine("  health openai       - Check OpenAI API status");
            Console.WriteLine();
            Console.WriteLine("This checks:");
            Console.WriteLine("  - CLI installation and version");
            Console.WriteLine("  - Authentication status");
            Console.WriteLine("  - API connectivity");
            Console.WriteLine("  - Subscription type (where detectable)");
            break;
    }
}

static void PrintSingleProviderHealth(ProviderHealth health)
{
    Console.WriteLine();
    Console.WriteLine($"{health.DisplayName}");
    Console.WriteLine("─".PadRight(50, '─'));

    var statusColor = health.Status switch
    {
        HealthStatus.Healthy => ConsoleColor.Green,
        HealthStatus.PartiallyHealthy => ConsoleColor.Yellow,
        _ => ConsoleColor.Red
    };

    Console.Write("Status: ");
    var prevColor = Console.ForegroundColor;
    Console.ForegroundColor = statusColor;
    Console.WriteLine(health.Status);
    Console.ForegroundColor = prevColor;

    if (!string.IsNullOrEmpty(health.Message))
        Console.WriteLine($"Message: {health.Message}");

    if (!string.IsNullOrEmpty(health.Version))
        Console.WriteLine($"Version: {health.Version}");

    if (!string.IsNullOrEmpty(health.ExecutablePath))
        Console.WriteLine($"Path: {health.ExecutablePath}");

    if (!string.IsNullOrEmpty(health.SubscriptionType))
        Console.WriteLine($"Subscription: {health.SubscriptionType}");

    Console.WriteLine($"Authenticated: {(health.IsAuthenticated ? "Yes" : "No")}");

    if (health.Details.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("Details:");
        foreach (var (key, value) in health.Details)
        {
            Console.WriteLine($"  {key}: {value}");
        }
    }
}

// ============================================================================
// Notification Commands
// ============================================================================
static async Task HandleNotifyCommandAsync(string[] args)
{
    var manager = new NotificationManager();
    var subCommand = args.Length > 1 ? args[1].ToLower() : "status";

    switch (subCommand)
    {
        case "status":
            Console.WriteLine("Notification Channels:");
            Console.WriteLine("─".PadRight(60, '─'));

            foreach (var channel in manager.Channels)
            {
                var configured = channel.IsConfigured ? "Configured" : "Not configured";
                var enabled = manager.EnabledChannels.Contains(channel.ChannelId) ? "Enabled" : "Disabled";
                var status = channel.IsConfigured && manager.EnabledChannels.Contains(channel.ChannelId)
                    ? ConsoleColor.Green : ConsoleColor.Yellow;

                Console.Write($"  {channel.DisplayName,-25} ");
                var prevColor = Console.ForegroundColor;
                Console.ForegroundColor = status;
                Console.Write($"{configured,-15}");
                Console.ForegroundColor = prevColor;
                Console.WriteLine($" {enabled}");
            }

            Console.WriteLine();
            Console.WriteLine("Setup Instructions:");
            Console.WriteLine("  Email:   Set SMTP_SERVER, SMTP_FROM, SMTP_PASSWORD, NOTIFY_EMAIL");
            Console.WriteLine("  Discord: Set DISCORD_WEBHOOK_URL");
            Console.WriteLine("  ntfy:    Set NTFY_TOPIC (free, no account needed)");
            break;

        case "test":
            Console.WriteLine("Testing notification channels...");
            Console.WriteLine();

            var testResults = await manager.TestAllChannelsAsync();

            foreach (var (channelId, success) in testResults)
            {
                var channel = manager.Channels.FirstOrDefault(c => c.ChannelId == channelId);
                var status = success ? "OK" : "FAILED";
                var color = success ? ConsoleColor.Green : ConsoleColor.Red;

                Console.Write($"  {channel?.DisplayName ?? channelId,-25} ");
                var prevColor = Console.ForegroundColor;
                Console.ForegroundColor = color;
                Console.WriteLine(status);
                Console.ForegroundColor = prevColor;
            }

            if (testResults.Count == 0)
            {
                Console.WriteLine("  No channels configured. Set environment variables first.");
            }
            break;

        case "enable":
            if (args.Length < 3)
            {
                Console.WriteLine("Usage: notify enable <channel>");
                Console.WriteLine("Channels: email, discord, ntfy");
                return;
            }
            var enableId = args[2].ToLower();
            manager.EnableChannel(enableId);
            Console.WriteLine($"Enabled notification channel: {enableId}");
            break;

        case "disable":
            if (args.Length < 3)
            {
                Console.WriteLine("Usage: notify disable <channel>");
                Console.WriteLine("Channels: email, discord, ntfy");
                return;
            }
            var disableId = args[2].ToLower();
            manager.DisableChannel(disableId);
            Console.WriteLine($"Disabled notification channel: {disableId}");
            break;

        case "config":
            Console.WriteLine("Notification Configuration:");
            Console.WriteLine("─".PadRight(60, '─'));
            Console.WriteLine();

            Console.WriteLine("Enabled Channels:");
            if (manager.EnabledChannels.Count == 0)
                Console.WriteLine("  (none)");
            else
                foreach (var ch in manager.EnabledChannels)
                    Console.WriteLine($"  - {ch}");

            Console.WriteLine();
            Console.WriteLine("Channel Configuration:");

            // Email
            var smtpServer = Environment.GetEnvironmentVariable("SMTP_SERVER");
            var smtpFrom = Environment.GetEnvironmentVariable("SMTP_FROM");
            var notifyEmail = Environment.GetEnvironmentVariable("NOTIFY_EMAIL");
            Console.WriteLine($"  Email:");
            Console.WriteLine($"    SMTP_SERVER:  {(string.IsNullOrEmpty(smtpServer) ? "(not set)" : smtpServer)}");
            Console.WriteLine($"    SMTP_FROM:    {(string.IsNullOrEmpty(smtpFrom) ? "(not set)" : smtpFrom)}");
            Console.WriteLine($"    NOTIFY_EMAIL: {(string.IsNullOrEmpty(notifyEmail) ? "(not set)" : notifyEmail)}");

            // Discord
            var discordUrl = Environment.GetEnvironmentVariable("DISCORD_WEBHOOK_URL");
            Console.WriteLine($"  Discord:");
            Console.WriteLine($"    DISCORD_WEBHOOK_URL: {(string.IsNullOrEmpty(discordUrl) ? "(not set)" : "(set)")}");

            // ntfy
            var ntfyTopic = Environment.GetEnvironmentVariable("NTFY_TOPIC");
            var ntfyServer = Environment.GetEnvironmentVariable("NTFY_SERVER");
            Console.WriteLine($"  ntfy:");
            Console.WriteLine($"    NTFY_TOPIC:  {(string.IsNullOrEmpty(ntfyTopic) ? "(not set)" : ntfyTopic)}");
            Console.WriteLine($"    NTFY_SERVER: {(string.IsNullOrEmpty(ntfyServer) ? "https://ntfy.sh (default)" : ntfyServer)}");
            break;

        case "send":
            // Manual send for testing
            if (args.Length < 3)
            {
                Console.WriteLine("Usage: notify send \"message\" [title]");
                return;
            }
            var message = args[2];
            var title = args.Length > 3 ? args[3] : "Test Notification";

            var result = await manager.SendAsync(new Notification
            {
                Title = title,
                Message = message,
                Level = NotificationLevel.Info,
                Type = NotificationType.General
            });

            if (result.Success)
                Console.WriteLine("Notification sent successfully!");
            else
                Console.WriteLine("Failed to send notification. Check channel configuration.");
            break;

        default:
            Console.WriteLine("Notification Commands:");
            Console.WriteLine("  notify                    - Show channel status");
            Console.WriteLine("  notify test               - Test all configured channels");
            Console.WriteLine("  notify enable <channel>   - Enable a channel (email, discord, ntfy)");
            Console.WriteLine("  notify disable <channel>  - Disable a channel");
            Console.WriteLine("  notify config             - Show full configuration");
            Console.WriteLine("  notify send \"msg\" [title] - Send a manual notification");
            Console.WriteLine();
            Console.WriteLine("Quick Setup (ntfy - free phone notifications):");
            Console.WriteLine("  1. Install ntfy app on your phone");
            Console.WriteLine("  2. Subscribe to a topic (e.g., 'my-automation')");
            Console.WriteLine("  3. Set environment variable: NTFY_TOPIC=my-automation");
            Console.WriteLine("  4. Run: dotnet run -- notify enable ntfy");
            Console.WriteLine("  5. Test: dotnet run -- notify test");
            break;
    }
}

// ============================================================================
// Config Commands - JSON configuration management
// ============================================================================
static void HandleConfigCommand(string[] args)
{
    var subCommand = args.Length > 1 ? args[1].ToLower() : "help";

    switch (subCommand)
    {
        case "sample":
            Console.WriteLine(TaskConfigLoader.CreateSampleJson());
            break;

        case "create":
            var outputPath = args.Length > 2 ? args[2] : "task-config.json";
            CreateConfigInteractive(outputPath);
            break;

        default:
            Console.WriteLine("Config Commands:");
            Console.WriteLine("  config sample           - Output sample JSON config to stdout");
            Console.WriteLine("  config create [file]    - Create a config file (default: task-config.json)");
            Console.WriteLine();
            Console.WriteLine("Example: dotnet run -- config sample > my-task.json");
            break;
    }
}

static void CreateConfigInteractive(string outputPath)
{
    Console.WriteLine("Creating task configuration...");
    Console.WriteLine();

    Console.Write("Task name: ");
    var name = Console.ReadLine() ?? "Untitled Task";

    Console.Write("Description (what to build): ");
    var description = Console.ReadLine() ?? "";

    Console.Write("Target path [./output]: ");
    var targetPath = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(targetPath)) targetPath = "./output";

    Console.Write("Scenario (new/update/add) [new]: ");
    var scenarioInput = Console.ReadLine()?.ToLower() ?? "new";
    var scenario = scenarioInput switch
    {
        "update" => BuildScenario.UpdateRepo,
        "add" => BuildScenario.AddToRepo,
        _ => BuildScenario.NewRepo
    };

    Console.Write("Strategy (first/efficient/thorough/balanced) [balanced]: ");
    var strategyInput = Console.ReadLine()?.ToLower() ?? "balanced";
    var strategy = strategyInput switch
    {
        "first" => DecisionStrategy.FirstOption,
        "efficient" => DecisionStrategy.Efficiency,
        "thorough" => DecisionStrategy.Thorough,
        _ => DecisionStrategy.Balanced
    };

    Console.Write("Model (haiku/sonnet/opus) [sonnet]: ");
    var model = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(model)) model = "sonnet";

    var config = new TaskConfig
    {
        Name = name,
        Description = description,
        TargetPath = targetPath,
        Scenario = scenario,
        Strategy = strategy,
        Model = model
    };

    TaskConfigLoader.Save(config, outputPath);
    Console.WriteLine();
    Console.WriteLine($"Config saved to: {outputPath}");
    Console.WriteLine($"Run it with: dotnet run -- run-config {outputPath}");
}

static void HandleTasksCommand(string[] args)
{
    var registry = new TaskRegistry();
    var subCommand = args.Length > 1 ? args[1].ToLower() : "list";

    switch (subCommand)
    {
        case "list":
            var tasks = registry.GetRecentTasks(20);
            if (tasks.Count == 0)
            {
                Console.WriteLine("No tasks found.");
                return;
            }

            Console.WriteLine("Recent Tasks:");
            Console.WriteLine("─".PadRight(80, '─'));
            Console.WriteLine($"{"ID",-10} {"State",-12} {"Duration",-10} {"Cost",-10} {"Name"}");
            Console.WriteLine("─".PadRight(80, '─'));

            foreach (var task in tasks)
            {
                var duration = task.Duration.HasValue ? $"{task.Duration.Value.TotalSeconds:F0}s" : "-";
                var cost = $"${task.CostUsd:F4}";
                var state = task.State.ToString();
                Console.WriteLine($"{task.TaskId,-10} {state,-12} {duration,-10} {cost,-10} {task.Name}");
            }
            break;

        case "status":
            if (args.Length < 3)
            {
                Console.WriteLine("Usage: dotnet run -- tasks status <task-id>");
                return;
            }
            var taskId = args[2];
            var status = registry.GetStatus(taskId);
            if (status == null)
            {
                Console.WriteLine($"Task not found: {taskId}");
                return;
            }

            Console.WriteLine($"Task: {status.TaskId}");
            Console.WriteLine($"Name: {status.Name}");
            Console.WriteLine($"State: {status.State}");
            Console.WriteLine($"Started: {status.StartedAt:yyyy-MM-dd HH:mm:ss}");
            if (status.CompletedAt.HasValue)
                Console.WriteLine($"Completed: {status.CompletedAt:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine($"Duration: {status.Duration?.TotalSeconds:F1}s");
            Console.WriteLine($"Cost: ${status.CostUsd:F4}");
            Console.WriteLine($"Steps: {status.StepsCompleted}/{status.TotalSteps}");
            if (!string.IsNullOrEmpty(status.CurrentStep))
                Console.WriteLine($"Current: {status.CurrentStep}");
            if (!string.IsNullOrEmpty(status.ErrorMessage))
                Console.WriteLine($"Error: {status.ErrorMessage}");

            if (status.Log.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("Recent Log:");
                foreach (var line in status.Log.TakeLast(10))
                    Console.WriteLine($"  {line}");
            }
            break;

        case "clear":
            var cleared = 0;
            foreach (var task in registry.GetStatuses().Where(t => t.State is TaskState.Completed or TaskState.Failed).ToList())
            {
                registry.RemoveTask(task.TaskId);
                cleared++;
            }
            Console.WriteLine($"Cleared {cleared} completed/failed tasks.");
            break;

        case "running":
            var running = registry.GetRunningTasks();
            if (running.Count == 0)
            {
                Console.WriteLine("No running tasks.");
                return;
            }
            Console.WriteLine("Running Tasks:");
            foreach (var task in running)
            {
                Console.WriteLine($"  {task.TaskId}: {task.Name} ({task.CurrentStep ?? "..."})");
            }
            break;

        default:
            Console.WriteLine("Task Commands:");
            Console.WriteLine("  tasks              - List recent tasks");
            Console.WriteLine("  tasks running      - Show running tasks");
            Console.WriteLine("  tasks status <id>  - Show detailed status");
            Console.WriteLine("  tasks clear        - Clear completed tasks");
            break;
    }
}

static async Task RunFromConfigAsync(string[] args, ILoggerFactory loggerFactory)
{
    if (args.Length < 2)
    {
        Console.WriteLine("Usage: dotnet run -- run-config <config-file.json>");
        return;
    }

    var configPath = args[1];
    if (!File.Exists(configPath))
    {
        Console.WriteLine($"Config file not found: {configPath}");
        return;
    }

    Console.WriteLine($"Loading config from: {configPath}");

    TaskConfig taskConfig;
    try
    {
        taskConfig = TaskConfigLoader.Load(configPath);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error loading config: {ex.Message}");
        return;
    }

    var buildConfig = taskConfig.ToBuildConfig();

    // Resolve relative path
    if (!Path.IsPathRooted(buildConfig.TargetPath))
    {
        buildConfig.TargetPath = Path.GetFullPath(buildConfig.TargetPath);
    }

    Console.WriteLine($"Task:             {taskConfig.Name}");
    Console.WriteLine($"ID:               {taskConfig.Id}");
    Console.WriteLine($"Target directory: {buildConfig.TargetPath}");
    Console.WriteLine();

    using var client = new ClaudeCodeClient(
        defaultWorkingDir: buildConfig.TargetPath,
        logger: loggerFactory.CreateLogger<ClaudeCodeClient>());

    var registry = new TaskRegistry();

    // Save config to registry
    registry.AddConfig(taskConfig);

    var builder = new AutoBuilder(
        client,
        buildConfig,
        registry,
        taskConfig.Id,
        loggerFactory.CreateLogger<AutoBuilder>());

    var result = await builder.RunAsync();

    if (result.Success)
    {
        Console.WriteLine();
        Console.WriteLine($"Build complete: {buildConfig.TargetPath}");
    }
}

// ============================================================================
// Auto Builder - Autonomous building with scenarios
// ============================================================================
static async Task RunAutoBuilderAsync(string[] args, ILoggerFactory loggerFactory)
{
    // Parse command line arguments
    // Format: auto [scenario] [strategy] [scope] "description" [target-path]

    var config = new BuildConfig
    {
        Scenario = BuildScenario.NewRepo,
        Strategy = DecisionStrategy.Balanced,
        Scope = BuildScope.FullBuild,
        Description = "",
        TargetPath = "",
        Model = Environment.GetEnvironmentVariable("CLAUDE_MODEL") ?? "sonnet",
        FullyAutonomous = true
    };

    // Parse args (skip "auto")
    var argList = args.Skip(1).ToList();
    var descriptionParts = new List<string>();
    string? targetPath = null;

    foreach (var arg in argList)
    {
        var lower = arg.ToLower();

        // Check for scenarios
        if (lower is "new" or "newrepo")
            config.Scenario = BuildScenario.NewRepo;
        else if (lower is "update" or "updaterepo")
            config.Scenario = BuildScenario.UpdateRepo;
        else if (lower is "add" or "addtorepo")
            config.Scenario = BuildScenario.AddToRepo;

        // Check for strategies
        else if (lower is "first" or "firstoption")
            config.Strategy = DecisionStrategy.FirstOption;
        else if (lower is "efficient" or "efficiency")
            config.Strategy = DecisionStrategy.Efficiency;
        else if (lower is "thorough" or "full")
            config.Strategy = DecisionStrategy.Thorough;
        else if (lower is "balanced")
            config.Strategy = DecisionStrategy.Balanced;

        // Check for scopes
        else if (lower is "component" or "single" or "singlecomponent")
            config.Scope = BuildScope.SingleComponent;
        else if (lower is "core" or "coreonly")
            config.Scope = BuildScope.CoreOnly;
        else if (lower is "fullbuild" or "complete")
            config.Scope = BuildScope.FullBuild;

        // Check if it looks like a path
        else if (arg.Contains('/') || arg.Contains('\\') || arg.StartsWith("./") || arg.StartsWith(".\\"))
            targetPath = arg;

        // Otherwise it's part of the description
        else
            descriptionParts.Add(arg);
    }

    config.Description = string.Join(" ", descriptionParts);
    config.TargetPath = Path.GetFullPath(targetPath ?? Path.Combine(Environment.CurrentDirectory, "generated",
        ExtractAppName(config.Description)));

    // Validate
    if (string.IsNullOrWhiteSpace(config.Description))
    {
        Console.WriteLine("Error: Description is required");
        Console.WriteLine();
        Console.WriteLine("Usage: dotnet run -- auto [scenario] [strategy] [scope] \"description\" [path]");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  dotnet run -- auto new \"A calculator CLI\"");
        Console.WriteLine("  dotnet run -- auto update efficient \"Fix the login bug\" ./myapp");
        Console.WriteLine("  dotnet run -- auto add component \"Add export feature\" ./project");
        return;
    }

    // For update/add scenarios, verify target exists
    if (config.Scenario != BuildScenario.NewRepo && !Directory.Exists(config.TargetPath))
    {
        Console.WriteLine($"Error: Target path does not exist: {config.TargetPath}");
        Console.WriteLine("For update/add scenarios, the target directory must already exist.");
        return;
    }

    Console.WriteLine("Starting autonomous build...");
    Console.WriteLine($"  Scenario:    {config.Scenario}");
    Console.WriteLine($"  Strategy:    {config.Strategy}");
    Console.WriteLine($"  Scope:       {config.Scope}");
    Console.WriteLine($"  Description: {config.Description}");
    Console.WriteLine($"  Target:      {config.TargetPath}");
    Console.WriteLine();

    using var client = new ClaudeCodeClient(
        defaultWorkingDir: config.TargetPath,
        logger: loggerFactory.CreateLogger<ClaudeCodeClient>());

    var builder = new AutoBuilder(
        client,
        config,
        loggerFactory.CreateLogger<AutoBuilder>());

    var result = await builder.RunAsync();

    // Output result summary
    if (result.Success)
    {
        Console.WriteLine();
        Console.WriteLine("Your build is ready at: " + config.TargetPath);

        if (result.BuildSuccess)
        {
            Console.WriteLine("Run it with: dotnet run");
        }
    }
}

// ============================================================================
// Helper Functions
// ============================================================================
static string Truncate(string text, int maxLength)
{
    if (string.IsNullOrEmpty(text)) return "";
    text = text.Replace("\n", " ").Replace("\r", "");
    return text.Length <= maxLength ? text : text[..(maxLength - 3)] + "...";
}
