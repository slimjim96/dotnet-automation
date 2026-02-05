using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace HumanLayerAutomation;

/// <summary>
/// Autonomous application builder with configurable scenarios and strategies.
/// Supports creating new repos, updating existing code, and adding new features.
/// </summary>
public class AutoBuilder
{
    private readonly ClaudeCodeClient _client;
    private readonly ILogger? _logger;
    private readonly BuildConfig _config;
    private readonly TaskRegistry? _registry;
    private readonly string _taskId;
    private decimal _totalCost;
    private readonly List<string> _buildLog = [];
    private TaskStatus? _status;

    public AutoBuilder(ClaudeCodeClient client, BuildConfig config, ILogger? logger = null)
        : this(client, config, null, null, logger)
    {
    }

    public AutoBuilder(ClaudeCodeClient client, BuildConfig config, TaskRegistry? registry, string? taskId, ILogger? logger = null)
    {
        _client = client;
        _config = config;
        _registry = registry;
        _taskId = taskId ?? Guid.NewGuid().ToString("N")[..8];
        _logger = logger;
    }

    /// <summary>The task ID for this build.</summary>
    public string TaskId => _taskId;

    /// <summary>
    /// Run the automated build process.
    /// </summary>
    public async Task<AutoBuildResult> RunAsync(CancellationToken ct = default)
    {
        var result = new AutoBuildResult
        {
            TaskId = _taskId,
            Config = _config,
            StartTime = DateTime.UtcNow
        };

        // Initialize status tracking
        _status = new TaskStatus
        {
            TaskId = _taskId,
            Name = _config.Description.Length > 50 ? _config.Description[..47] + "..." : _config.Description,
            State = TaskState.Running,
            StartedAt = DateTime.UtcNow
        };
        UpdateRegistryStatus();

        Log("══════════════════════════════════════════════════════════════");
        Log($"AUTO BUILDER - {_config.Scenario} [{_taskId}]");
        Log("══════════════════════════════════════════════════════════════");
        Log($"Target:   {_config.TargetPath}");
        Log($"Strategy: {_config.Strategy}");
        Log($"Scope:    {_config.Scope}");
        Log($"Model:    {_config.Model}");
        Log("");

        try
        {
            // Ensure target directory exists
            Directory.CreateDirectory(_config.TargetPath);

            // Phase 1: Analyze (for update/add scenarios)
            if (_config.Scenario != BuildScenario.NewRepo)
            {
                Log("PHASE 1: Analyzing existing codebase...");
                var analysis = await AnalyzeExistingCodeAsync(ct);
                result.CodebaseAnalysis = analysis;
                Log($"  Found {analysis.FileCount} files, {analysis.ProjectType}");
                Log("");
            }

            // Phase 2: Plan
            Log(_config.Scenario == BuildScenario.NewRepo
                ? "PHASE 1: Planning..."
                : "PHASE 2: Planning...");
            var plan = await CreatePlanAsync(result.CodebaseAnalysis, ct);
            result.Plan = plan;

            if (!plan.Success)
            {
                result.ErrorMessage = "Planning failed: " + plan.ErrorMessage;
                return FinalizeResult(result);
            }

            Log($"  Plan: {plan.Steps.Count} steps");
            foreach (var step in plan.Steps)
            {
                Log($"    - {step}");
            }
            Log("");

            // Check cost limit
            if (_config.MaxCostUsd > 0 && _totalCost >= _config.MaxCostUsd)
            {
                result.ErrorMessage = $"Cost limit reached: ${_totalCost:F4} >= ${_config.MaxCostUsd:F4}";
                return FinalizeResult(result);
            }

            // Phase 3: Execute
            Log(_config.Scenario == BuildScenario.NewRepo
                ? "PHASE 2: Executing plan..."
                : "PHASE 3: Executing plan...");

            foreach (var step in plan.Steps)
            {
                if (ct.IsCancellationRequested) break;

                Log($"  → {step}");
                var stepResult = await ExecuteStepAsync(step, result.CodebaseAnalysis, ct);
                result.ExecutedSteps.Add(stepResult);

                if (!stepResult.Success)
                {
                    Log($"    ✗ Failed: {stepResult.ErrorMessage}");
                    // Try to recover
                    if (!await TryRecoverAsync(stepResult, ct))
                    {
                        result.ErrorMessage = $"Step failed: {step}";
                        return FinalizeResult(result);
                    }
                    Log($"    ✓ Recovered");
                }
                else
                {
                    Log($"    ✓ Done (${stepResult.Cost:F4})");
                }

                // Check cost limit
                if (_config.MaxCostUsd > 0 && _totalCost >= _config.MaxCostUsd)
                {
                    Log($"  Cost limit reached, stopping early");
                    break;
                }
            }
            Log("");

            // Phase 4: Build & Fix (if applicable)
            if (HasBuildableProject())
            {
                Log(_config.Scenario == BuildScenario.NewRepo
                    ? "PHASE 3: Building..."
                    : "PHASE 4: Building...");

                var buildSuccess = await BuildWithAutoFixAsync(ct);
                result.BuildSuccess = buildSuccess;

                Log(buildSuccess ? "  ✓ Build successful" : "  ✗ Build failed");
                Log("");
            }

            result.Success = result.BuildSuccess || !HasBuildableProject();
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
            _logger?.LogError(ex, "Build failed with exception");
        }

        return FinalizeResult(result);
    }

    /// <summary>
    /// Analyze an existing codebase to understand its structure and conventions.
    /// </summary>
    private async Task<CodebaseAnalysis> AnalyzeExistingCodeAsync(CancellationToken ct)
    {
        var analysis = new CodebaseAnalysis();

        // Count files
        var files = Directory.GetFiles(_config.TargetPath, "*.*", SearchOption.AllDirectories)
            .Where(f => !f.Contains("bin") && !f.Contains("obj") && !f.Contains(".git"))
            .ToList();
        analysis.FileCount = files.Count;

        // Detect project type
        if (files.Any(f => f.EndsWith(".csproj")))
        {
            analysis.ProjectType = "dotnet";
            analysis.ProjectFiles = files.Where(f => f.EndsWith(".csproj")).ToList();
        }
        else if (files.Any(f => f.EndsWith("package.json")))
        {
            analysis.ProjectType = "node";
        }
        else if (files.Any(f => f.EndsWith(".py")))
        {
            analysis.ProjectType = "python";
        }
        else
        {
            analysis.ProjectType = "unknown";
        }

        // Get code conventions via Claude (for non-trivial codebases)
        if (files.Count > 2 && _config.Scenario != BuildScenario.NewRepo)
        {
            var prompt = $@"
{GetContextPrompts()}

Analyze the codebase at {_config.TargetPath} and identify:
1. Naming conventions (files, classes, methods)
2. Code organization patterns
3. Common utilities or helpers
4. Any patterns I should follow when adding/modifying code

Be brief - 3-5 bullet points maximum.
";

            var result = await RunClaudeAsync(prompt, allowedTools: ["Read", "Glob", "Grep"], ct: ct);
            if (result.Success)
            {
                analysis.Conventions = ExtractContent(result.Output);
            }
        }

        return analysis;
    }

    /// <summary>
    /// Create a plan for the build.
    /// </summary>
    private async Task<BuildPlan> CreatePlanAsync(CodebaseAnalysis? analysis, CancellationToken ct)
    {
        var plan = new BuildPlan();

        var contextInfo = analysis != null
            ? $"\nExisting codebase: {analysis.FileCount} files, {analysis.ProjectType} project\nConventions: {analysis.Conventions ?? "N/A"}\n"
            : "";

        var newRepoInstructions = _config.Scenario == BuildScenario.NewRepo ? @"
IMPORTANT for new .NET projects:
- Step 1 MUST be: Create .csproj project file (for .NET 8, console app)
- Include proper project structure" : "";

        var prompt = $@"
{GetContextPrompts()}
{contextInfo}

Task: {_config.Description}

Create a step-by-step plan to accomplish this. Each step should be:
- A single, clear action (e.g., ""Create Program.cs with main entry point"")
- Achievable in one code generation call
- Listed in dependency order
{newRepoInstructions}

{(_config.Scope == BuildScope.SingleComponent ? $"Focus only on: {_config.FocusComponent}" : "")}

Output ONLY a numbered list of steps, nothing else. Example:
1. Create .csproj project file
2. Create Program.cs with main logic
3. Add helper class if needed
";

        var result = await RunClaudeAsync(prompt, ct: ct);

        if (!result.Success)
        {
            plan.ErrorMessage = result.Error;
            return plan;
        }

        // Parse steps from response
        var content = ExtractContent(result.Output) ?? "";
        var stepMatches = Regex.Matches(content, @"^\d+\.\s*(.+)$", RegexOptions.Multiline);

        foreach (Match match in stepMatches)
        {
            plan.Steps.Add(match.Groups[1].Value.Trim());
        }

        if (plan.Steps.Count == 0)
        {
            // Fallback: treat each line as a step
            plan.Steps = content.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim().TrimStart('0', '1', '2', '3', '4', '5', '6', '7', '8', '9', '.', '-', ' '))
                .Where(l => l.Length > 5)
                .ToList();
        }

        plan.Success = plan.Steps.Count > 0;
        return plan;
    }

    /// <summary>
    /// Execute a single step of the plan.
    /// </summary>
    private async Task<StepResult> ExecuteStepAsync(string step, CodebaseAnalysis? analysis, CancellationToken ct)
    {
        var stepResult = new StepResult { Step = step, StartTime = DateTime.UtcNow };

        // Determine what kind of step this is
        var isFileCreation = step.Contains("Create", StringComparison.OrdinalIgnoreCase) ||
                            step.Contains("Add", StringComparison.OrdinalIgnoreCase) ||
                            step.Contains("Generate", StringComparison.OrdinalIgnoreCase);

        var isModification = step.Contains("Update", StringComparison.OrdinalIgnoreCase) ||
                            step.Contains("Modify", StringComparison.OrdinalIgnoreCase) ||
                            step.Contains("Change", StringComparison.OrdinalIgnoreCase) ||
                            step.Contains("Fix", StringComparison.OrdinalIgnoreCase);

        string prompt;
        string[] allowedTools;

        if (isFileCreation || isModification)
        {
            prompt = $@"
{GetContextPrompts()}

Working directory: {_config.TargetPath}

Task: {step}

Context: {_config.Description}

{(analysis?.Conventions != null ? $"Follow these conventions:\n{analysis.Conventions}" : "")}

Execute this step by creating or modifying the necessary files.
Use the Write or Edit tool to make changes.
";
            allowedTools = ["Read", "Glob", "Grep", "Write", "Edit", "Bash"];
        }
        else
        {
            // Analysis or other step
            prompt = $@"
{GetContextPrompts()}

Working directory: {_config.TargetPath}

Task: {step}

Execute this step. If it requires code changes, use the appropriate tools.
";
            allowedTools = ["Read", "Glob", "Grep", "Write", "Edit", "Bash"];
        }

        var result = await RunClaudeAsync(prompt, allowedTools: allowedTools, ct: ct);

        stepResult.Cost = result.CostUsd ?? 0;
        stepResult.Success = result.Success;
        stepResult.Output = result.Output;
        stepResult.ErrorMessage = result.Error;
        stepResult.EndTime = DateTime.UtcNow;

        return stepResult;
    }

    /// <summary>
    /// Try to recover from a failed step.
    /// </summary>
    private async Task<bool> TryRecoverAsync(StepResult failedStep, CancellationToken ct)
    {
        for (int attempt = 1; attempt <= _config.MaxRetries; attempt++)
        {
            var prompt = $@"
{GetContextPrompts()}

The previous step failed:
Step: {failedStep.Step}
Error: {failedStep.ErrorMessage}
Output: {failedStep.Output}

Please analyze the error and fix it. This is attempt {attempt} of {_config.MaxRetries}.
";

            var result = await RunClaudeAsync(prompt,
                allowedTools: ["Read", "Glob", "Grep", "Write", "Edit", "Bash"],
                ct: ct);

            if (result.Success)
            {
                failedStep.RecoveryAttempts = attempt;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Build the project and auto-fix errors.
    /// </summary>
    private async Task<bool> BuildWithAutoFixAsync(CancellationToken ct)
    {
        for (int attempt = 1; attempt <= _config.MaxBuildFixes; attempt++)
        {
            var buildOutput = await RunBuildAsync(ct);

            if (buildOutput.Success)
                return true;

            Log($"    Build attempt {attempt} failed, auto-fixing...");

            var prompt = $@"
{GetContextPrompts()}

Build failed with errors:
{buildOutput.Output}

Working directory: {_config.TargetPath}

Fix all build errors. Use Edit or Write tools to modify the files.
";

            var result = await RunClaudeAsync(prompt,
                allowedTools: ["Read", "Glob", "Write", "Edit"],
                ct: ct);

            if (!result.Success)
            {
                Log($"      Fix attempt failed: {result.Error}");
            }
        }

        return false;
    }

    private async Task<(bool Success, string Output)> RunBuildAsync(CancellationToken ct)
    {
        var projectFiles = Directory.GetFiles(_config.TargetPath, "*.csproj", SearchOption.TopDirectoryOnly);
        if (projectFiles.Length == 0)
            return (true, "No project file to build");

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = "build",
            WorkingDirectory = _config.TargetPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)!;
        var output = await process.StandardOutput.ReadToEndAsync(ct);
        var error = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        return (process.ExitCode == 0, output + error);
    }

    private bool HasBuildableProject()
    {
        return Directory.GetFiles(_config.TargetPath, "*.csproj", SearchOption.TopDirectoryOnly).Length > 0;
    }

    private async Task<ClaudeResult> RunClaudeAsync(string prompt, string[]? allowedTools = null, CancellationToken ct = default)
    {
        var options = new ClaudeOptions
        {
            Model = _config.Model,
            MaxTurns = 20,
            AutoApprove = _config.FullyAutonomous,
            WorkingDir = _config.TargetPath,
            AllowedTools = allowedTools?.ToList(),
            OutputFormat = "json"
        };

        var result = await _client.RunAsync(prompt, options, ct);
        _totalCost += result.CostUsd ?? 0;
        return result;
    }

    private string GetContextPrompts()
    {
        return $@"
{_config.GetScenarioPrompt()}
{_config.GetStrategyPrompt()}
{_config.GetScopePrompt()}
";
    }

    private string? ExtractContent(string? jsonOutput)
    {
        if (string.IsNullOrWhiteSpace(jsonOutput))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(jsonOutput);
            var root = doc.RootElement;

            if (root.TryGetProperty("result", out var result))
                return result.GetString();
            if (root.TryGetProperty("content", out var content))
                return content.GetString();
        }
        catch (JsonException)
        {
            return jsonOutput;
        }

        return jsonOutput;
    }

    private void Log(string message)
    {
        _buildLog.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
        Console.WriteLine(message);

        // Update status log
        if (_status != null)
        {
            _status.Log.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
        }
    }

    private void UpdateStatus(string? currentStep = null, int? stepsCompleted = null, int? totalSteps = null)
    {
        if (_status == null) return;

        if (currentStep != null) _status.CurrentStep = currentStep;
        if (stepsCompleted.HasValue) _status.StepsCompleted = stepsCompleted.Value;
        if (totalSteps.HasValue) _status.TotalSteps = totalSteps.Value;
        _status.CostUsd = _totalCost;

        UpdateRegistryStatus();
    }

    private void UpdateRegistryStatus()
    {
        if (_registry != null && _status != null)
        {
            _registry.UpdateStatus(_status);
        }
    }

    private AutoBuildResult FinalizeResult(AutoBuildResult result)
    {
        result.EndTime = DateTime.UtcNow;
        result.TotalCost = _totalCost;
        result.BuildLog = _buildLog.ToList();

        // Update final status
        if (_status != null)
        {
            _status.State = result.Success ? TaskState.Completed : TaskState.Failed;
            _status.CompletedAt = DateTime.UtcNow;
            _status.Success = result.Success;
            _status.CostUsd = _totalCost;
            _status.ErrorMessage = result.ErrorMessage;
            _status.Log = _buildLog.ToList();
            UpdateRegistryStatus();
        }

        Log("");
        Log("══════════════════════════════════════════════════════════════");
        Log(result.Success ? "BUILD SUCCEEDED" : "BUILD FAILED");
        Log("══════════════════════════════════════════════════════════════");
        Log($"Task ID:   {_taskId}");
        Log($"Duration:  {result.Duration.TotalSeconds:F1}s");
        Log($"Cost:      ${result.TotalCost:F4}");
        Log($"Steps:     {result.ExecutedSteps.Count}");

        if (!string.IsNullOrEmpty(result.ErrorMessage))
            Log($"Error:     {result.ErrorMessage}");

        return result;
    }
}

// ============================================================================
// Result Models
// ============================================================================

public class AutoBuildResult
{
    public string TaskId { get; set; } = "";
    public bool Success { get; set; }
    public bool BuildSuccess { get; set; }
    public BuildConfig Config { get; set; } = null!;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public TimeSpan Duration => EndTime - StartTime;
    public decimal TotalCost { get; set; }
    public string? ErrorMessage { get; set; }
    public CodebaseAnalysis? CodebaseAnalysis { get; set; }
    public BuildPlan? Plan { get; set; }
    public List<StepResult> ExecutedSteps { get; set; } = [];
    public List<string> BuildLog { get; set; } = [];
}

public class CodebaseAnalysis
{
    public int FileCount { get; set; }
    public string ProjectType { get; set; } = "unknown";
    public List<string> ProjectFiles { get; set; } = [];
    public string? Conventions { get; set; }
}

public class BuildPlan
{
    public bool Success { get; set; }
    public List<string> Steps { get; set; } = [];
    public string? ErrorMessage { get; set; }
}

public class StepResult
{
    public string Step { get; set; } = "";
    public bool Success { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public TimeSpan Duration => EndTime - StartTime;
    public decimal Cost { get; set; }
    public string? Output { get; set; }
    public string? ErrorMessage { get; set; }
    public int RecoveryAttempts { get; set; }
}
