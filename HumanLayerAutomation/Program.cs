using Microsoft.Extensions.Logging;
using HumanLayerAutomation;

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
    Console.WriteLine("  run        - Run a single task: dotnet run -- run \"your prompt\"");
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
    Console.WriteLine("Environment Variables:");
    Console.WriteLine("  CLAUDE_PATH         - Path to claude executable (default: claude)");
    Console.WriteLine("  WORKING_DIR         - Default working directory");
    Console.WriteLine("  CLAUDE_MODEL        - Default model (opus, sonnet, haiku)");
    Console.WriteLine("  OUTPUT_DIR          - Output directory for build commands");
    Console.WriteLine();
    Console.WriteLine("Examples:");
    Console.WriteLine("  dotnet run -- demo");
    Console.WriteLine("  dotnet run -- run \"List files in current directory\"");
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
        Console.WriteLine("Usage: dotnet run -- run \"your prompt\"");
        Console.WriteLine();
        Console.WriteLine("Options (via environment variables):");
        Console.WriteLine("  CLAUDE_MODEL=haiku|sonnet|opus");
        Console.WriteLine("  MAX_TURNS=10");
        Console.WriteLine("  AUTO_APPROVE=true|false");
        return;
    }

    var prompt = args[1];
    var model = Environment.GetEnvironmentVariable("CLAUDE_MODEL") ?? "sonnet";
    var maxTurns = int.TryParse(Environment.GetEnvironmentVariable("MAX_TURNS"), out var mt) ? mt : 20;
    var autoApprove = Environment.GetEnvironmentVariable("AUTO_APPROVE")?.ToLower() == "true";
    var workingDir = Environment.GetEnvironmentVariable("WORKING_DIR") ?? Environment.CurrentDirectory;

    Console.WriteLine($"Running task with Claude ({model})...");
    Console.WriteLine($"Prompt: {Truncate(prompt, 100)}");
    Console.WriteLine($"Auto-approve: {autoApprove}");
    Console.WriteLine();

    using var client = new ClaudeCodeClient(
        defaultWorkingDir: workingDir,
        logger: loggerFactory.CreateLogger<ClaudeCodeClient>());

    client.DefaultModel = model;

    var result = await client.RunAsync(
        prompt: prompt,
        options: new ClaudeOptions
        {
            MaxTurns = maxTurns,
            AutoApprove = autoApprove,
            OutputFormat = "json"
        });

    Console.WriteLine($"\n=== Result ===");
    Console.WriteLine($"Status: {(result.Success ? "Success" : "Failed")}");
    Console.WriteLine($"Duration: {result.Duration.TotalSeconds:F1}s");

    if (result.CostUsd.HasValue)
        Console.WriteLine($"Cost: ${result.CostUsd:F4}");

    if (result.InputTokens.HasValue)
        Console.WriteLine($"Tokens: {result.InputTokens} in / {result.OutputTokens} out");

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

    Console.WriteLine($"Building app from description: {description}");
    Console.WriteLine($"Output directory: {outputDir}");
    Console.WriteLine();

    var workingDir = Environment.GetEnvironmentVariable("WORKING_DIR") ?? Environment.CurrentDirectory;

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

    var outputDir = Environment.GetEnvironmentVariable("OUTPUT_DIR")
        ?? Path.Combine(Environment.CurrentDirectory, "generated", "TodoApp");

    var workingDir = Environment.GetEnvironmentVariable("WORKING_DIR") ?? Environment.CurrentDirectory;

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

    Console.WriteLine($"Task: {taskConfig.Name}");
    Console.WriteLine($"ID: {taskConfig.Id}");
    Console.WriteLine();

    var buildConfig = taskConfig.ToBuildConfig();

    // Resolve relative path
    if (!Path.IsPathRooted(buildConfig.TargetPath))
    {
        buildConfig.TargetPath = Path.GetFullPath(buildConfig.TargetPath);
    }

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
    config.TargetPath = targetPath ?? Path.Combine(Environment.CurrentDirectory, "generated",
        ExtractAppName(config.Description));

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
