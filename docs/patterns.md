# Automation Patterns

Best practices and patterns for building reliable AI automation with human oversight.

## Pattern Categories

1. **Approval Patterns** - How to handle human review
2. **Execution Patterns** - How to run AI tasks
3. **Scheduling Patterns** - When to run tasks
4. **Error Handling Patterns** - How to handle failures
5. **Cost Optimization Patterns** - How to manage token usage

---

## Approval Patterns

### Pattern 1: Fully Automated (Trusted Tasks)

For safe, read-only operations that don't need human review.

```csharp
var task = new AutomationTask
{
    Name = "Code Analysis",
    Query = "Analyze code complexity and identify potential issues",
    AutoApprove = true,
    AutoApproveTimeout = TimeSpan.FromMinutes(5),
    AllowedTools = new[] { "Read", "Glob", "Grep" }  // Whitelist safe tools only
};

var result = await client.RunTaskAsync(task);
```

**When to use**:
- Analysis tasks that don't modify anything
- Information gathering
- Report generation from existing data

**Safety considerations**:
- Always whitelist allowed tools
- Set reasonable timeouts
- Review results periodically

---

### Pattern 2: Human-in-the-Loop (Sensitive Tasks)

Every tool invocation requires explicit human approval.

```csharp
var task = new AutomationTask
{
    Name = "Production Deployment",
    Query = "Deploy the latest changes to production",
    AutoApprove = false  // Every tool use requires approval
};

// Launch task
var sessionData = await client.CreateSessionAsync(new CreateSessionRequest
{
    Query = task.Query,
    WorkingDir = "/app",
    DangerouslySkipPermissions = false
});

// Monitor and approve in real-time
await client.SubscribeToEventsAsync(
    onEvent: async evt => {
        if (evt.Type == "new_approval")
        {
            var approvalId = evt.Data["approval_id"].ToString();
            var approval = await client.GetApprovalAsync(approvalId);

            // Present to human for review
            DisplayApprovalUI(approval);
        }
    },
    eventTypes: new[] { "new_approval" }
);
```

**When to use**:
- Production changes
- Security-sensitive operations
- Irreversible actions

---

### Pattern 3: Tiered Approval (Hybrid)

Auto-approve safe operations, require human review for dangerous ones.

```csharp
public class TieredApprovalProcessor
{
    private readonly HumanLayerClient _client;

    private readonly HashSet<string> _autoApproveTools = new()
    {
        "Read", "Glob", "Grep", "LS", "WebSearch"
    };

    private readonly HashSet<string> _dangerousTools = new()
    {
        "Bash", "Write", "Edit", "NotebookEdit"
    };

    public async Task ProcessApprovalsAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var pending = await _client.GetPendingApprovalsAsync(ct: ct);

            foreach (var approval in pending)
            {
                var tool = approval.ToolName ?? "unknown";

                if (_autoApproveTools.Contains(tool))
                {
                    await _client.ApproveAsync(approval.Id, "Auto-approved (safe tool)");
                    Console.WriteLine($"✓ Auto-approved: {tool}");
                }
                else if (_dangerousTools.Contains(tool))
                {
                    // Present for human review
                    var decision = await PromptHumanAsync(approval);
                    if (decision.Approved)
                        await _client.ApproveAsync(approval.Id, decision.Comment);
                    else
                        await _client.DenyAsync(approval.Id, decision.Reason);
                }
                else
                {
                    // Unknown tool - require review
                    Console.WriteLine($"⚠ Unknown tool requires review: {tool}");
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(5), ct);
        }
    }
}
```

---

### Pattern 4: Batch Approval (Periodic Review)

Collect approvals and process them in batches.

```csharp
public class BatchApprovalProcessor
{
    private readonly HumanLayerClient _client;
    private readonly TimeSpan _batchInterval = TimeSpan.FromMinutes(15);

    public async Task RunBatchCycleAsync()
    {
        while (true)
        {
            var pending = await _client.GetPendingApprovalsAsync();

            if (pending.Count > 0)
            {
                Console.WriteLine($"\n=== Approval Batch ({pending.Count} items) ===\n");

                foreach (var approval in pending)
                {
                    DisplayApproval(approval);
                    Console.Write("Decision (a=approve, d=deny, s=skip): ");

                    var key = Console.ReadKey();
                    Console.WriteLine();

                    switch (char.ToLower(key.KeyChar))
                    {
                        case 'a':
                            await _client.ApproveAsync(approval.Id);
                            break;
                        case 'd':
                            Console.Write("Reason: ");
                            var reason = Console.ReadLine();
                            await _client.DenyAsync(approval.Id, reason ?? "Denied");
                            break;
                        case 's':
                            // Leave for next batch
                            break;
                    }
                }
            }

            Console.WriteLine($"Next batch in {_batchInterval.TotalMinutes} minutes...");
            await Task.Delay(_batchInterval);
        }
    }
}
```

---

## Execution Patterns

### Pattern 5: Fire and Forget

Launch a task and don't wait for completion.

```csharp
// Launch session
var session = await client.CreateSessionAsync(new CreateSessionRequest
{
    Query = "Generate weekly report",
    DangerouslySkipPermissions = true,
    DangerouslySkipPermissionsTimeoutMs = 600000  // 10-minute timeout
});

Console.WriteLine($"Launched session: {session.SessionId}");
// Process exits, session continues in daemon
```

---

### Pattern 6: Launch and Wait

Block until the session completes.

```csharp
var session = await client.LaunchAndWaitAsync(
    new CreateSessionRequest
    {
        Query = "Analyze codebase structure",
        Model = "haiku"
    },
    timeout: TimeSpan.FromMinutes(5)
);

Console.WriteLine($"Completed with status: {session.Status}");
Console.WriteLine($"Cost: ${session.CostUsd:F4}");
```

---

### Pattern 7: Parallel Execution

Run multiple independent tasks concurrently.

```csharp
var tasks = new[]
{
    new AutomationTask { Name = "Task 1", Query = "Analyze frontend code" },
    new AutomationTask { Name = "Task 2", Query = "Analyze backend code" },
    new AutomationTask { Name = "Task 3", Query = "Check dependencies" }
};

// Launch all in parallel
var results = await Task.WhenAll(
    tasks.Select(t => client.RunTaskAsync(t))
);

// Aggregate results
var totalCost = results.Sum(r => r.CostUsd ?? 0);
var allSucceeded = results.All(r => r.Status == "completed");

Console.WriteLine($"All tasks succeeded: {allSucceeded}");
Console.WriteLine($"Total cost: ${totalCost:F4}");
```

---

### Pattern 8: Sequential Pipeline

Chain tasks where each depends on the previous.

```csharp
public async Task<string> RunPipelineAsync(string projectDir)
{
    // Stage 1: Analysis
    var analysisSession = await client.LaunchAndWaitAsync(new CreateSessionRequest
    {
        Query = "Analyze this codebase and identify the main components",
        WorkingDir = projectDir,
        Model = "haiku",
        DangerouslySkipPermissions = true
    });

    if (analysisSession.Status != "completed")
        throw new Exception($"Analysis failed: {analysisSession.ErrorMessage}");

    // Stage 2: Planning (uses analysis context)
    var planSession = await client.ContinueSessionAsync(
        analysisSession.Id,
        "Based on your analysis, create an implementation plan for adding authentication"
    );

    var planResult = await client.WaitForSessionAsync(planSession.SessionId);

    // Stage 3: Implementation (human-reviewed)
    var implSession = await client.ContinueSessionAsync(
        planResult.Id,
        "Now implement the plan step by step"
    );

    // This stage requires human approval for code changes
    return implSession.SessionId;
}
```

---

## Scheduling Patterns

### Pattern 9: Cron-Style Scheduling

Run tasks on regular intervals.

```csharp
public class TaskScheduler
{
    private readonly HumanLayerClient _client;
    private readonly List<ScheduledTask> _tasks = new();

    public void Schedule(string name, TimeSpan interval, AutomationTask task)
    {
        _tasks.Add(new ScheduledTask(name, interval, task, DateTime.MinValue));
    }

    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;

            foreach (var scheduled in _tasks)
            {
                if (now - scheduled.LastRun >= scheduled.Interval)
                {
                    Console.WriteLine($"[{now:HH:mm}] Running: {scheduled.Name}");

                    try
                    {
                        var result = await _client.RunTaskAsync(scheduled.Task, ct);
                        scheduled.LastRun = now;

                        LogResult(scheduled.Name, result);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error in {scheduled.Name}: {ex.Message}");
                        // Don't update LastRun - will retry next cycle
                    }
                }
            }

            await Task.Delay(TimeSpan.FromMinutes(1), ct);
        }
    }
}

// Usage
var scheduler = new TaskScheduler(client);

scheduler.Schedule("Hourly Analysis", TimeSpan.FromHours(1), new AutomationTask
{
    Name = "Code Review",
    Query = "Review recent changes for issues",
    AutoApprove = true
});

scheduler.Schedule("Daily Report", TimeSpan.FromHours(24), new AutomationTask
{
    Name = "Daily Summary",
    Query = "Generate a summary of today's code changes",
    AutoApprove = true
});

await scheduler.RunAsync(cancellationToken);
```

---

### Pattern 10: Event-Driven Execution

Trigger tasks based on external events.

```csharp
// Trigger on file system changes
var watcher = new FileSystemWatcher("/project/src");
watcher.Changed += async (sender, e) =>
{
    await client.CreateSessionAsync(new CreateSessionRequest
    {
        Query = $"Review the changes to {e.Name} and suggest improvements",
        Model = "haiku",
        DangerouslySkipPermissions = true,
        DangerouslySkipPermissionsTimeoutMs = 60000
    });
};
watcher.EnableRaisingEvents = true;
```

---

## Error Handling Patterns

### Pattern 11: Retry with Exponential Backoff

Handle transient failures gracefully.

```csharp
public async Task<T> WithRetryAsync<T>(
    Func<Task<T>> operation,
    int maxRetries = 3,
    TimeSpan? initialDelay = null)
{
    var delay = initialDelay ?? TimeSpan.FromSeconds(1);

    for (int attempt = 1; attempt <= maxRetries; attempt++)
    {
        try
        {
            return await operation();
        }
        catch (HttpRequestException ex) when (attempt < maxRetries)
        {
            Console.WriteLine($"Attempt {attempt} failed: {ex.Message}");
            Console.WriteLine($"Retrying in {delay.TotalSeconds}s...");

            await Task.Delay(delay);
            delay *= 2;  // Exponential backoff
        }
    }

    throw new Exception("All retry attempts failed");
}

// Usage
var session = await WithRetryAsync(() =>
    client.CreateSessionAsync(request)
);
```

---

### Pattern 12: Circuit Breaker

Stop calling a failing service temporarily.

```csharp
public class CircuitBreaker
{
    private int _failureCount = 0;
    private DateTime _openUntil = DateTime.MinValue;
    private readonly int _threshold = 5;
    private readonly TimeSpan _timeout = TimeSpan.FromMinutes(1);

    public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation)
    {
        if (DateTime.UtcNow < _openUntil)
        {
            throw new Exception("Circuit breaker is open");
        }

        try
        {
            var result = await operation();
            _failureCount = 0;  // Reset on success
            return result;
        }
        catch
        {
            _failureCount++;
            if (_failureCount >= _threshold)
            {
                _openUntil = DateTime.UtcNow + _timeout;
                Console.WriteLine($"Circuit breaker opened until {_openUntil}");
            }
            throw;
        }
    }
}
```

---

## Cost Optimization Patterns

### Pattern 13: Model Selection by Complexity

Use cheaper models for simple tasks.

```csharp
public string SelectModel(AutomationTask task)
{
    // Simple analysis tasks
    if (task.Query.Contains("list") ||
        task.Query.Contains("count") ||
        task.Query.Contains("simple"))
    {
        return "haiku";  // Cheapest
    }

    // Standard development work
    if (task.Query.Contains("implement") ||
        task.Query.Contains("refactor"))
    {
        return "sonnet";  // Balanced
    }

    // Complex reasoning
    if (task.Query.Contains("architect") ||
        task.Query.Contains("design") ||
        task.Query.Contains("complex"))
    {
        return "opus";  // Most capable
    }

    return "sonnet";  // Default
}
```

---

### Pattern 14: Token Budget Management

Track and limit token usage.

```csharp
public class TokenBudgetManager
{
    private decimal _dailyBudget;
    private decimal _spent;
    private DateTime _resetDate;

    public TokenBudgetManager(decimal dailyBudgetUsd)
    {
        _dailyBudget = dailyBudgetUsd;
        _spent = 0;
        _resetDate = DateTime.UtcNow.Date.AddDays(1);
    }

    public bool CanSpend(decimal estimatedCost)
    {
        ResetIfNeeded();
        return _spent + estimatedCost <= _dailyBudget;
    }

    public void RecordSpend(decimal cost)
    {
        _spent += cost;
        Console.WriteLine($"Budget: ${_spent:F2} / ${_dailyBudget:F2}");
    }

    private void ResetIfNeeded()
    {
        if (DateTime.UtcNow >= _resetDate)
        {
            _spent = 0;
            _resetDate = DateTime.UtcNow.Date.AddDays(1);
        }
    }
}

// Usage
var budget = new TokenBudgetManager(dailyBudgetUsd: 10.00m);

if (budget.CanSpend(estimatedCost: 0.50m))
{
    var result = await client.RunTaskAsync(task);
    budget.RecordSpend(result.CostUsd ?? 0);
}
else
{
    Console.WriteLine("Daily budget exceeded, skipping task");
}
```

---

### Pattern 15: Tool Whitelisting

Limit tools to reduce unnecessary token usage.

```csharp
// For analysis tasks, only allow read operations
var analysisTask = new AutomationTask
{
    Query = "Analyze code quality",
    AllowedTools = new[] { "Read", "Glob", "Grep" },
    DisallowedTools = new[] { "Bash", "Write", "Edit" }
};

// For focused implementation, limit to specific tools
var implementTask = new AutomationTask
{
    Query = "Fix the authentication bug in auth.ts",
    AllowedTools = new[] { "Read", "Edit" },  // Only read and edit
    MaxTurns = 10  // Limit iterations
};
```

---

## Anti-Patterns to Avoid

### ❌ Unlimited Auto-Approve

```csharp
// DON'T: No timeout or tool restrictions
DangerouslySkipPermissions = true
// Without timeout, this runs indefinitely with no oversight
```

### ✅ Safe Auto-Approve

```csharp
// DO: Set timeout and limit tools
DangerouslySkipPermissions = true,
DangerouslySkipPermissionsTimeoutMs = 300000,  // 5 minutes
AllowedTools = new[] { "Read", "Glob", "Grep" }
```

---

### ❌ Ignoring Failures

```csharp
// DON'T: Silently ignore errors
try
{
    await client.RunTaskAsync(task);
}
catch { /* ignored */ }
```

### ✅ Handle and Log Failures

```csharp
// DO: Log and potentially retry
try
{
    await client.RunTaskAsync(task);
}
catch (Exception ex)
{
    logger.LogError(ex, "Task {Name} failed", task.Name);
    await AlertOperatorAsync(task, ex);
}
```

---

## Next Steps

- [Security](security.md) - Security best practices
- [Getting Started](getting-started.md) - Setup guide
- [API Reference](api-reference.md) - Complete API documentation
