using Microsoft.Extensions.Logging;
using HumanLayerAutomation;

namespace Examples;

/// <summary>
/// Example: Using Claude Code automation to build a simple Todo CLI application.
///
/// This demonstrates a multi-step automated build process where:
/// 1. Claude designs the application structure
/// 2. Claude generates the code
/// 3. Human reviews/approves each step
/// 4. Code is written to disk
///
/// Run with: dotnet run --project HumanLayerAutomation -- build-app
/// </summary>
public class BuildSimpleAppExample
{
    private readonly ClaudeCodeClient _client;
    private readonly ILogger _logger;
    private readonly string _outputDir;

    public BuildSimpleAppExample(ClaudeCodeClient client, ILogger logger, string outputDir)
    {
        _client = client;
        _logger = logger;
        _outputDir = outputDir;
    }

    /// <summary>
    /// Run the full automated build process.
    /// </summary>
    public async Task<BuildResult> RunAsync(CancellationToken ct = default)
    {
        var result = new BuildResult { StartTime = DateTime.UtcNow };

        Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║   Automated App Builder - Using Claude Code Automation       ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
        Console.WriteLine();

        // Step 1: Design the application
        Console.WriteLine("STEP 1: Designing the application...");
        var designResult = await DesignApplicationAsync(ct);
        if (!designResult.Success)
        {
            result.ErrorMessage = $"Design failed: {designResult.Error}";
            return result;
        }
        result.DesignOutput = designResult.Output;
        Console.WriteLine($"  ✓ Design complete ({designResult.Duration.TotalSeconds:F1}s)");
        Console.WriteLine();

        // Step 2: Generate the main program code
        Console.WriteLine("STEP 2: Generating main program code...");
        var codeResult = await GenerateMainCodeAsync(ct);
        if (!codeResult.Success)
        {
            result.ErrorMessage = $"Code generation failed: {codeResult.Error}";
            return result;
        }
        result.GeneratedCode = codeResult.Output;
        Console.WriteLine($"  ✓ Code generated ({codeResult.Duration.TotalSeconds:F1}s)");
        Console.WriteLine();

        // Step 3: Generate the model classes
        Console.WriteLine("STEP 3: Generating model classes...");
        var modelResult = await GenerateModelsAsync(ct);
        if (!modelResult.Success)
        {
            result.ErrorMessage = $"Model generation failed: {modelResult.Error}";
            return result;
        }
        result.ModelsCode = modelResult.Output;
        Console.WriteLine($"  ✓ Models generated ({modelResult.Duration.TotalSeconds:F1}s)");
        Console.WriteLine();

        // Step 4: Generate project file
        Console.WriteLine("STEP 4: Generating project configuration...");
        var projectResult = await GenerateProjectFileAsync(ct);
        if (!projectResult.Success)
        {
            result.ErrorMessage = $"Project file generation failed: {projectResult.Error}";
            return result;
        }
        result.ProjectFile = projectResult.Output;
        Console.WriteLine($"  ✓ Project file generated ({projectResult.Duration.TotalSeconds:F1}s)");
        Console.WriteLine();

        // Calculate totals
        result.EndTime = DateTime.UtcNow;
        result.Success = true;
        result.TotalCost = (designResult.CostUsd ?? 0) +
                          (codeResult.CostUsd ?? 0) +
                          (modelResult.CostUsd ?? 0) +
                          (projectResult.CostUsd ?? 0);

        return result;
    }

    /// <summary>
    /// Step 1: Design the application architecture.
    /// Uses read-only tools, safe to auto-approve.
    /// </summary>
    private async Task<ClaudeResult> DesignApplicationAsync(CancellationToken ct)
    {
        var prompt = @"
Design a simple command-line Todo application with these requirements:
- Add, list, complete, and delete todo items
- Store todos in a JSON file
- Support priorities (low, medium, high)
- Show completion status

Provide:
1. A brief architecture overview (2-3 sentences)
2. List of files to create
3. Key classes/types needed

Keep it minimal - no database, no web UI, just a simple CLI.
";

        return await _client.RunAsync(prompt, new ClaudeOptions
        {
            Model = "haiku",
            MaxTurns = 5,
            AutoApprove = true, // Read-only design, safe
            AllowedTools = ["Read", "Glob"] // No write access
        }, ct);
    }

    /// <summary>
    /// Step 2: Generate the main Program.cs code.
    /// Requires approval since we're generating code to be written.
    /// </summary>
    private async Task<ClaudeResult> GenerateMainCodeAsync(CancellationToken ct)
    {
        var prompt = @"
Generate a complete C# Program.cs for a Todo CLI app with these commands:
- todo add ""task"" [--priority high|medium|low]
- todo list [--all | --pending | --completed]
- todo complete <id>
- todo delete <id>

Requirements:
- Use top-level statements
- Parse arguments manually (no external libraries)
- Store todos in ~/todos.json
- Output should be clean and user-friendly

Output ONLY the C# code, no explanations.
";

        return await _client.RunAsync(prompt, new ClaudeOptions
        {
            Model = "sonnet", // Better model for code generation
            MaxTurns = 10,
            AutoApprove = true, // For demo; in production, set false
            AllowedTools = ["Read"] // Read-only for code generation
        }, ct);
    }

    /// <summary>
    /// Step 3: Generate the Todo model class.
    /// </summary>
    private async Task<ClaudeResult> GenerateModelsAsync(CancellationToken ct)
    {
        var prompt = @"
Generate a C# file 'TodoItem.cs' containing:
1. TodoItem record with: Id (int), Title (string), IsCompleted (bool), Priority (enum), CreatedAt (DateTime)
2. Priority enum: Low, Medium, High
3. TodoStore class that handles loading/saving from JSON file

Use System.Text.Json for serialization.
Output ONLY the C# code, no explanations.
";

        return await _client.RunAsync(prompt, new ClaudeOptions
        {
            Model = "sonnet",
            MaxTurns = 10,
            AutoApprove = true,
            AllowedTools = ["Read"]
        }, ct);
    }

    /// <summary>
    /// Step 4: Generate the .csproj file.
    /// </summary>
    private async Task<ClaudeResult> GenerateProjectFileAsync(CancellationToken ct)
    {
        var prompt = @"
Generate a minimal .csproj file for a .NET 8 console application called 'TodoCli'.
- Target framework: net8.0
- Output type: Exe
- No external NuGet packages needed
- Enable nullable reference types

Output ONLY the XML, no explanations.
";

        return await _client.RunAsync(prompt, new ClaudeOptions
        {
            Model = "haiku", // Simple task, use cheap model
            MaxTurns = 3,
            AutoApprove = true,
            AllowedTools = []
        }, ct);
    }
}

/// <summary>
/// Result of the automated build process.
/// </summary>
public class BuildResult
{
    public bool Success { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public TimeSpan Duration => EndTime - StartTime;
    public decimal TotalCost { get; set; }
    public string? ErrorMessage { get; set; }

    public string? DesignOutput { get; set; }
    public string? GeneratedCode { get; set; }
    public string? ModelsCode { get; set; }
    public string? ProjectFile { get; set; }
}
