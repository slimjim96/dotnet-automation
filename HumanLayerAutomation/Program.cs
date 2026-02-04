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
    Console.WriteLine();
    Console.WriteLine("Environment Variables:");
    Console.WriteLine("  CLAUDE_PATH         - Path to claude executable (default: claude)");
    Console.WriteLine("  WORKING_DIR         - Default working directory");
    Console.WriteLine("  CLAUDE_MODEL        - Default model (opus, sonnet, haiku)");
    Console.WriteLine();
    Console.WriteLine("Examples:");
    Console.WriteLine("  dotnet run -- demo");
    Console.WriteLine("  dotnet run -- run \"List files in current directory\"");
    Console.WriteLine("  dotnet run -- scheduler");
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
// Helper Functions
// ============================================================================
static string Truncate(string text, int maxLength)
{
    if (string.IsNullOrEmpty(text)) return "";
    text = text.Replace("\n", " ").Replace("\r", "");
    return text.Length <= maxLength ? text : text[..(maxLength - 3)] + "...";
}
