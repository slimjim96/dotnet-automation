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
Console.WriteLine("║       HumanLayer .NET Automation Examples                    ║");
Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
Console.WriteLine();

try
{
    switch (mode.ToLower())
    {
        case "demo":
            await RunDemoAsync(loggerFactory);
            break;
        case "batch":
            await RunBatchProcessorAsync(loggerFactory);
            break;
        case "scheduler":
            await RunSchedulerAsync(loggerFactory);
            break;
        case "monitor":
            await RunApprovalMonitorAsync(loggerFactory);
            break;
        case "parallel":
            await RunParallelTasksAsync(loggerFactory);
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
    Console.WriteLine("Usage: dotnet run -- <mode>");
    Console.WriteLine();
    Console.WriteLine("Modes:");
    Console.WriteLine("  demo       - Quick demonstration of API capabilities");
    Console.WriteLine("  batch      - Batch approval processor for periodic review");
    Console.WriteLine("  scheduler  - Scheduled task runner (cron-like)");
    Console.WriteLine("  monitor    - Real-time approval monitor with SSE");
    Console.WriteLine("  parallel   - Run multiple AI tasks in parallel");
    Console.WriteLine();
    Console.WriteLine("Environment Variables:");
    Console.WriteLine("  HUMANLAYER_URL      - Daemon URL (default: http://localhost:7777/api/v1)");
    Console.WriteLine("  OPENROUTER_API_KEY  - API key for OpenRouter proxy");
    Console.WriteLine("  WORKING_DIR         - Default working directory for sessions");
}

// ============================================================================
// Demo Mode - Quick API demonstration
// ============================================================================
static async Task RunDemoAsync(ILoggerFactory loggerFactory)
{
    Console.WriteLine("=== Demo Mode ===");
    Console.WriteLine("Demonstrating HumanLayer API capabilities...\n");

    var baseUrl = Environment.GetEnvironmentVariable("HUMANLAYER_URL") ?? "http://localhost:7777/api/v1";
    using var client = new HumanLayerClient(baseUrl, loggerFactory.CreateLogger<HumanLayerClient>());

    // Check daemon health
    Console.WriteLine("1. Checking daemon health...");
    try
    {
        var health = await client.HealthAsync();
        Console.WriteLine($"   Status: {health.Status}, Version: {health.Version ?? "unknown"}");
    }
    catch (HttpRequestException)
    {
        Console.WriteLine("   ERROR: Cannot connect to daemon. Is 'hld' running?");
        Console.WriteLine("   Start it with: hld daemon start");
        return;
    }

    // List existing sessions
    Console.WriteLine("\n2. Listing existing sessions...");
    var sessions = await client.ListSessionsAsync();
    Console.WriteLine($"   Found {sessions.Count} active sessions");

    foreach (var session in sessions.Take(3))
    {
        Console.WriteLine($"   - [{session.Status}] {session.Id}: {Truncate(session.Query ?? session.Title ?? "No query", 50)}");
    }

    // List pending approvals
    Console.WriteLine("\n3. Checking pending approvals...");
    var approvals = await client.GetPendingApprovalsAsync();
    Console.WriteLine($"   Found {approvals.Count} pending approvals");

    foreach (var approval in approvals.Take(5))
    {
        Console.WriteLine($"   - {approval.Id}: {approval.ToolName} (Session: {approval.SessionId})");
    }

    // Example: Create a simple session (commented out to avoid accidental execution)
    Console.WriteLine("\n4. Session creation example (dry run):");
    Console.WriteLine("   Would create session with query: 'List files in current directory'");
    Console.WriteLine("   To actually run: uncomment the code in Program.cs");

    /*
    var sessionData = await client.CreateSessionAsync(new CreateSessionRequest
    {
        Query = "List the files in the current directory",
        WorkingDir = Environment.CurrentDirectory,
        Model = "haiku", // Use cheapest model for demo
        MaxTurns = 5,
        DangerouslySkipPermissions = true, // Auto-approve for demo
        DangerouslySkipPermissionsTimeoutMs = 60000 // 1 minute timeout
    });
    Console.WriteLine($"   Created session: {sessionData.SessionId}");

    var finalSession = await client.WaitForSessionAsync(sessionData.SessionId, TimeSpan.FromMinutes(2));
    Console.WriteLine($"   Final status: {finalSession.Status}");
    Console.WriteLine($"   Cost: ${finalSession.CostUsd:F4}");
    */

    Console.WriteLine("\n=== Demo Complete ===");
}

// ============================================================================
// Batch Processor - Periodic approval review
// ============================================================================
static async Task RunBatchProcessorAsync(ILoggerFactory loggerFactory)
{
    Console.WriteLine("=== Batch Approval Processor ===");
    Console.WriteLine("This mode processes pending approvals in batches.\n");

    var baseUrl = Environment.GetEnvironmentVariable("HUMANLAYER_URL") ?? "http://localhost:7777/api/v1";
    using var client = new HumanLayerClient(baseUrl, loggerFactory.CreateLogger<HumanLayerClient>());

    var safeTools = new HashSet<string>
    {
        "Read", "Glob", "Grep", "LS", "WebFetch", "WebSearch"
    };

    var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
        Console.WriteLine("\nShutting down...");
    };

    Console.WriteLine("Processing approvals. Press Ctrl+C to stop.\n");
    Console.WriteLine("Auto-approving safe tools: " + string.Join(", ", safeTools));
    Console.WriteLine("Other tools require manual review.\n");

    var batchInterval = TimeSpan.FromSeconds(10);
    var processedCount = 0;
    var manualReviewCount = 0;

    while (!cts.Token.IsCancellationRequested)
    {
        try
        {
            var approvals = await client.GetPendingApprovalsAsync(ct: cts.Token);

            if (approvals.Count > 0)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Found {approvals.Count} pending approvals");

                foreach (var approval in approvals)
                {
                    var toolName = approval.ToolName ?? "unknown";

                    if (safeTools.Contains(toolName))
                    {
                        await client.ApproveAsync(approval.Id, "Auto-approved (safe tool)", cts.Token);
                        Console.WriteLine($"  ✓ Auto-approved: {approval.Id} ({toolName})");
                        processedCount++;
                    }
                    else
                    {
                        Console.WriteLine($"  ⚠ Manual review needed: {approval.Id} ({toolName})");
                        Console.WriteLine($"    Input: {FormatToolInput(approval.ToolInput)}");
                        Console.Write("    Approve? (y/n/s=skip): ");

                        var key = Console.ReadKey();
                        Console.WriteLine();

                        switch (char.ToLower(key.KeyChar))
                        {
                            case 'y':
                                await client.ApproveAsync(approval.Id, "Manually approved", cts.Token);
                                Console.WriteLine($"    ✓ Approved");
                                processedCount++;
                                break;
                            case 'n':
                                Console.Write("    Denial reason: ");
                                var reason = Console.ReadLine() ?? "Denied by operator";
                                await client.DenyAsync(approval.Id, reason, cts.Token);
                                Console.WriteLine($"    ✗ Denied");
                                processedCount++;
                                break;
                            default:
                                Console.WriteLine($"    → Skipped (will review later)");
                                manualReviewCount++;
                                break;
                        }
                    }
                }
            }

            await Task.Delay(batchInterval, cts.Token);
        }
        catch (OperationCanceledException)
        {
            break;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            await Task.Delay(5000, cts.Token);
        }
    }

    Console.WriteLine($"\nProcessed {processedCount} approvals, {manualReviewCount} skipped for later.");
}

// ============================================================================
// Scheduler - Run tasks on a schedule
// ============================================================================
static async Task RunSchedulerAsync(ILoggerFactory loggerFactory)
{
    Console.WriteLine("=== Task Scheduler ===");
    Console.WriteLine("Running scheduled AI tasks with human oversight.\n");

    var baseUrl = Environment.GetEnvironmentVariable("HUMANLAYER_URL") ?? "http://localhost:7777/api/v1";
    var workingDir = Environment.GetEnvironmentVariable("WORKING_DIR") ?? Environment.CurrentDirectory;

    using var client = new HumanLayerClient(baseUrl, loggerFactory.CreateLogger<HumanLayerClient>());

    // Example scheduled tasks
    var tasks = new List<(TimeSpan interval, AutomationTask task)>
    {
        (TimeSpan.FromHours(1), new AutomationTask
        {
            Name = "Code Review Summary",
            Query = "Review any new or modified files in this directory and provide a brief summary of changes",
            WorkingDir = workingDir,
            Model = "haiku",
            MaxTurns = 10,
            AutoApprove = true, // Safe read-only task
            AutoApproveTimeout = TimeSpan.FromMinutes(5),
            AllowedTools = ["Read", "Glob", "Grep"]
        }),
        (TimeSpan.FromHours(4), new AutomationTask
        {
            Name = "Security Scan",
            Query = "Scan for potential security issues, hardcoded secrets, or vulnerable dependencies",
            WorkingDir = workingDir,
            Model = "sonnet",
            MaxTurns = 20,
            AutoApprove = true,
            AutoApproveTimeout = TimeSpan.FromMinutes(10),
            AllowedTools = ["Read", "Glob", "Grep", "Bash(grep*)"] // Limited bash
        }),
        (TimeSpan.FromHours(24), new AutomationTask
        {
            Name = "Documentation Update",
            Query = "Review code and update any outdated documentation or add missing docstrings",
            WorkingDir = workingDir,
            Model = "sonnet",
            MaxTurns = 50,
            AutoApprove = false, // Requires human approval for edits
        })
    };

    var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
    };

    Console.WriteLine("Scheduled tasks:");
    foreach (var (interval, task) in tasks)
    {
        Console.WriteLine($"  - {task.Name}: Every {interval.TotalHours}h");
    }
    Console.WriteLine("\nPress Ctrl+C to stop.\n");

    // Track last run times
    var lastRuns = new Dictionary<string, DateTime>();
    foreach (var (_, task) in tasks)
    {
        lastRuns[task.Name] = DateTime.MinValue;
    }

    while (!cts.Token.IsCancellationRequested)
    {
        var now = DateTime.UtcNow;

        foreach (var (interval, task) in tasks)
        {
            if (now - lastRuns[task.Name] >= interval)
            {
                Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm}] Running: {task.Name}");

                try
                {
                    var result = await client.RunTaskAsync(task, cts.Token);
                    lastRuns[task.Name] = now;

                    Console.WriteLine($"  Status: {result.Status}");
                    Console.WriteLine($"  Duration: {result.Duration.TotalSeconds:F1}s");
                    if (result.CostUsd.HasValue)
                        Console.WriteLine($"  Cost: ${result.CostUsd:F4}");
                    if (!string.IsNullOrEmpty(result.Summary))
                        Console.WriteLine($"  Summary: {Truncate(result.Summary, 100)}");
                    Console.WriteLine();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  Error: {ex.Message}");
                    // Don't update last run on error, will retry next cycle
                }
            }
        }

        await Task.Delay(TimeSpan.FromMinutes(1), cts.Token);
    }
}

// ============================================================================
// Monitor - Real-time approval monitoring with SSE
// ============================================================================
static async Task RunApprovalMonitorAsync(ILoggerFactory loggerFactory)
{
    Console.WriteLine("=== Real-time Approval Monitor ===");
    Console.WriteLine("Watching for approval events via SSE.\n");

    var baseUrl = Environment.GetEnvironmentVariable("HUMANLAYER_URL") ?? "http://localhost:7777/api/v1";
    using var client = new HumanLayerClient(baseUrl, loggerFactory.CreateLogger<HumanLayerClient>());

    var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
    };

    Console.WriteLine("Listening for events. Press Ctrl+C to stop.\n");

    try
    {
        await client.SubscribeToEventsAsync(
            onEvent: evt =>
            {
                var timestamp = evt.Timestamp.ToString("HH:mm:ss");
                Console.WriteLine($"[{timestamp}] {evt.Type}");

                if (evt.Data != null)
                {
                    foreach (var (key, value) in evt.Data)
                    {
                        Console.WriteLine($"  {key}: {value}");
                    }
                }
                Console.WriteLine();
            },
            eventTypes: ["new_approval", "approval_resolved", "session_status_changed"],
            ct: cts.Token
        );
    }
    catch (OperationCanceledException)
    {
        Console.WriteLine("\nMonitor stopped.");
    }
}

// ============================================================================
// Parallel - Run multiple tasks simultaneously
// ============================================================================
static async Task RunParallelTasksAsync(ILoggerFactory loggerFactory)
{
    Console.WriteLine("=== Parallel Task Runner ===");
    Console.WriteLine("Running multiple AI tasks concurrently.\n");

    var baseUrl = Environment.GetEnvironmentVariable("HUMANLAYER_URL") ?? "http://localhost:7777/api/v1";
    var workingDir = Environment.GetEnvironmentVariable("WORKING_DIR") ?? Environment.CurrentDirectory;
    var openRouterKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");

    using var client = new HumanLayerClient(baseUrl, loggerFactory.CreateLogger<HumanLayerClient>());

    // Define parallel tasks
    var tasks = new List<AutomationTask>
    {
        new()
        {
            Name = "Task 1: File Analysis",
            Query = "Analyze the structure of this codebase and list the main components",
            WorkingDir = workingDir,
            Model = "haiku",
            MaxTurns = 10,
            AutoApprove = true,
            AutoApproveTimeout = TimeSpan.FromMinutes(2)
        },
        new()
        {
            Name = "Task 2: Dependency Check",
            Query = "List all external dependencies and their versions",
            WorkingDir = workingDir,
            Model = "haiku",
            MaxTurns = 10,
            AutoApprove = true,
            AutoApproveTimeout = TimeSpan.FromMinutes(2)
        }
    };

    // Optionally add an OpenRouter task
    if (!string.IsNullOrEmpty(openRouterKey))
    {
        tasks.Add(new AutomationTask
        {
            Name = "Task 3: OpenRouter Analysis",
            Query = "Provide a high-level overview of this project's purpose",
            WorkingDir = workingDir,
            MaxTurns = 10,
            AutoApprove = true,
            AutoApproveTimeout = TimeSpan.FromMinutes(2),
            ProxyBaseUrl = "https://openrouter.ai/api/v1",
            ProxyModel = "anthropic/claude-3-haiku", // or any OpenRouter model
            ProxyApiKey = openRouterKey
        });
    }

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
        Console.WriteLine();
    }

    Console.WriteLine($"Total wall-clock time: {totalDuration.TotalSeconds:F1}s");
    Console.WriteLine($"Total cost: ${totalCost:F4}");
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

static string FormatToolInput(Dictionary<string, object>? input)
{
    if (input == null || input.Count == 0) return "(empty)";
    return string.Join(", ", input.Select(kv => $"{kv.Key}={Truncate(kv.Value?.ToString() ?? "", 50)}"));
}
