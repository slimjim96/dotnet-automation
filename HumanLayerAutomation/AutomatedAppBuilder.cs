using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace HumanLayerAutomation;

/// <summary>
/// Automated application builder with self-healing capabilities.
/// Automatically detects errors and sends them back to Claude for correction.
/// </summary>
public class AutomatedAppBuilder
{
    private readonly ClaudeCodeClient _client;
    private readonly ILogger? _logger;
    private readonly BuilderOptions _options;

    public AutomatedAppBuilder(ClaudeCodeClient client, BuilderOptions? options = null, ILogger? logger = null)
    {
        _client = client;
        _options = options ?? new BuilderOptions();
        _logger = logger;
    }

    /// <summary>
    /// Build an application from a description with automatic error recovery.
    /// </summary>
    public async Task<AppBuildResult> BuildAsync(AppSpec spec, CancellationToken ct = default)
    {
        var result = new AppBuildResult
        {
            AppName = spec.Name,
            OutputDirectory = spec.OutputDirectory,
            StartTime = DateTime.UtcNow
        };

        Console.WriteLine();
        Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║   Automated App Builder with Self-Healing                    ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
        Console.WriteLine();

        // Ensure output directory exists
        Directory.CreateDirectory(spec.OutputDirectory);

        try
        {
            // Phase 1: Design
            Console.WriteLine("PHASE 1: Design");
            Console.WriteLine("─".PadRight(60, '─'));
            var design = await GenerateWithRetryAsync(
                "design",
                GetDesignPrompt(spec),
                null, // Design doesn't need code extraction
                ct);
            result.Steps.Add(design);
            if (!design.Success) { result.ErrorMessage = "Design phase failed"; return result; }
            Console.WriteLine($"  ✓ Design complete ({design.Duration.TotalSeconds:F1}s, ${design.Cost:F4})");
            Console.WriteLine();

            // Phase 2: Generate each file (with cross-file context)
            Console.WriteLine("PHASE 2: Code Generation");
            Console.WriteLine("─".PadRight(60, '─'));

            var files = GetFilesToGenerate(spec);
            var generatedFiles = new Dictionary<string, string>(); // fileName -> code

            foreach (var file in files)
            {
                Console.WriteLine($"  Generating {file.FileName}...");

                var codeStep = await GenerateCodeFileAsync(
                    file,
                    spec,
                    design.Output,
                    generatedFiles,
                    ct);

                result.Steps.Add(codeStep);

                if (codeStep.Success)
                {
                    var filePath = Path.Combine(spec.OutputDirectory, file.FileName);
                    await File.WriteAllTextAsync(filePath, codeStep.CleanedCode!, ct);
                    generatedFiles[file.FileName] = codeStep.CleanedCode!;
                    Console.WriteLine($"    ✓ {file.FileName} ({codeStep.CleanedCode!.Split('\n').Length} lines, ${codeStep.Cost:F4})");
                }
                else
                {
                    Console.WriteLine($"    ✗ {file.FileName} failed after {_options.MaxRetries} retries");
                    result.ErrorMessage = $"Failed to generate {file.FileName}";
                    return result;
                }
            }
            Console.WriteLine();

            // Phase 3: Build and fix errors
            Console.WriteLine("PHASE 3: Build & Auto-Fix");
            Console.WriteLine("─".PadRight(60, '─'));

            var buildResult = await BuildWithAutoFixAsync(spec, result.Steps, ct);
            result.Steps.AddRange(buildResult.Steps);
            result.BuildSuccess = buildResult.Success;

            if (buildResult.Success)
            {
                Console.WriteLine($"  ✓ Build successful after {buildResult.Attempts} attempt(s)");
            }
            else
            {
                Console.WriteLine($"  ✗ Build failed after {buildResult.Attempts} attempt(s)");
                result.ErrorMessage = "Build failed after maximum fix attempts";
            }
            Console.WriteLine();

            // Phase 4: Optional test run
            if (buildResult.Success && spec.TestCommands?.Length > 0)
            {
                Console.WriteLine("PHASE 4: Verification");
                Console.WriteLine("─".PadRight(60, '─'));

                foreach (var testCmd in spec.TestCommands)
                {
                    var testResult = await RunTestAsync(spec.OutputDirectory, testCmd, ct);
                    result.TestResults.Add(testResult);
                    var status = testResult.Success ? "✓" : "✗";
                    Console.WriteLine($"  {status} {testCmd}: {(testResult.Success ? "passed" : "failed")}");
                }
                Console.WriteLine();
            }

            result.Success = buildResult.Success;
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
            _logger?.LogError(ex, "Build failed with exception");
        }
        finally
        {
            result.EndTime = DateTime.UtcNow;
            result.TotalCost = result.Steps.Sum(s => s.Cost);
        }

        PrintSummary(result);
        return result;
    }

    /// <summary>
    /// Generate content with automatic retry on failure.
    /// </summary>
    private async Task<BuildStep> GenerateWithRetryAsync(
        string stepName,
        string prompt,
        Func<string, string?>? codeExtractor,
        CancellationToken ct)
    {
        var step = new BuildStep { Name = stepName, StartTime = DateTime.UtcNow };
        string? lastError = null;

        for (int attempt = 1; attempt <= _options.MaxRetries; attempt++)
        {
            step.Attempts = attempt;

            // Build prompt with error context if retrying
            var fullPrompt = lastError != null
                ? $"{prompt}\n\nPREVIOUS ATTEMPT FAILED WITH ERROR:\n{lastError}\n\nPlease fix the issue and try again."
                : prompt;

            var result = await _client.RunAsync(fullPrompt, new ClaudeOptions
            {
                Model = _options.Model,
                MaxTurns = _options.MaxTurns,
                AutoApprove = true,
                OutputFormat = "json",
                AppendSystemPrompt = ResponseValidator.NoQuestionsPrompt
            }, ct);

            step.Cost += result.CostUsd ?? 0;
            step.RawOutput = result.Output;

            if (!result.Success)
            {
                lastError = $"Claude returned error: {result.Error}";
                _logger?.LogWarning("Attempt {Attempt} failed: {Error}", attempt, lastError);
                continue;
            }

            // Detect permission-blocked responses
            if (result.IsPermissionRequest)
            {
                lastError = "You were blocked by tool permissions and could not write files. " +
                            "This is an automated pipeline with AutoApprove enabled — " +
                            "you have full permission to use Write, Edit, and Bash tools. " +
                            "Proceed with the implementation without asking for approval.";
                _logger?.LogWarning("Attempt {Attempt}: Claude was blocked by permissions", attempt);
                continue;
            }

            // Detect questioning responses (Claude asking for clarification instead of doing work)
            if (result.IsQuestioningResponse)
            {
                lastError = "You asked clarifying questions instead of completing the task. " +
                            "This is an automated pipeline with no human to respond. " +
                            "Make reasonable assumptions and proceed with the implementation. " +
                            "Do NOT ask questions — just produce the requested output.";
                _logger?.LogWarning("Attempt {Attempt}: Claude asked questions instead of completing work", attempt);
                continue;
            }

            // Extract the actual content from JSON response
            var content = ExtractContent(result.Output);
            if (string.IsNullOrWhiteSpace(content))
            {
                lastError = "Empty response from Claude";
                continue;
            }

            // If we have a code extractor, use it to clean the output
            if (codeExtractor != null)
            {
                var cleanedCode = codeExtractor(content);
                if (cleanedCode == null)
                {
                    lastError = "Failed to extract valid code from response";
                    continue;
                }
                step.CleanedCode = cleanedCode;
            }

            step.Output = content;
            step.Success = true;
            step.EndTime = DateTime.UtcNow;
            return step;
        }

        step.Success = false;
        step.ErrorMessage = lastError ?? "Unknown error";
        step.EndTime = DateTime.UtcNow;
        return step;
    }

    /// <summary>
    /// Generate a code file with validation and retry.
    /// Includes already-generated files as context so Claude can reference actual types/methods.
    /// </summary>
    private async Task<BuildStep> GenerateCodeFileAsync(
        FileSpec file,
        AppSpec spec,
        string? designContext,
        Dictionary<string, string> generatedFiles,
        CancellationToken ct)
    {
        var prompt = GetCodeGenerationPrompt(file, spec, designContext, generatedFiles);

        return await GenerateWithRetryAsync(
            $"generate:{file.FileName}",
            prompt,
            output => ExtractCode(output, file.FileType),
            ct);
    }

    /// <summary>
    /// Build the project and automatically fix errors.
    /// </summary>
    private async Task<BuildFixResult> BuildWithAutoFixAsync(
        AppSpec spec,
        List<BuildStep> existingSteps,
        CancellationToken ct)
    {
        var result = new BuildFixResult();

        for (int attempt = 1; attempt <= _options.MaxBuildFixAttempts; attempt++)
        {
            result.Attempts = attempt;

            // Run dotnet build
            var buildOutput = await RunBuildAsync(spec.OutputDirectory, ct);

            if (buildOutput.Success)
            {
                result.Success = true;
                return result;
            }

            Console.WriteLine($"  Build attempt {attempt} failed, analyzing errors...");

            // Parse build errors
            var errors = ParseBuildErrors(buildOutput.Output);
            if (errors.Count == 0)
            {
                result.ErrorMessage = "Build failed but couldn't parse errors";
                return result;
            }

            // Try to fix each file with errors
            foreach (var fileErrors in errors.GroupBy(e => e.FileName))
            {
                var fileName = fileErrors.Key;
                var filePath = Path.Combine(spec.OutputDirectory, fileName);

                if (!File.Exists(filePath))
                {
                    Console.WriteLine($"    Skipping {fileName} (file not found)");
                    continue;
                }

                var currentCode = await File.ReadAllTextAsync(filePath, ct);
                var errorList = string.Join("\n", fileErrors.Select(e => $"  Line {e.Line}: {e.Code} - {e.Message}"));

                Console.WriteLine($"    Fixing {fileName} ({fileErrors.Count()} errors)...");

                var fixStep = await GenerateWithRetryAsync(
                    $"fix:{fileName}",
                    GetFixPrompt(fileName, currentCode, errorList),
                    output => ExtractCode(output, GetFileType(fileName)),
                    ct);

                result.Steps.Add(fixStep);

                if (fixStep.Success && fixStep.CleanedCode != null)
                {
                    await File.WriteAllTextAsync(filePath, fixStep.CleanedCode, ct);
                    Console.WriteLine($"      ✓ Applied fix (${fixStep.Cost:F4})");
                }
                else
                {
                    Console.WriteLine($"      ✗ Fix generation failed");
                }
            }
        }

        result.ErrorMessage = $"Build still failing after {_options.MaxBuildFixAttempts} fix attempts";
        return result;
    }

    /// <summary>
    /// Extract content from Claude's JSON response.
    /// </summary>
    private string? ExtractContent(string jsonOutput)
    {
        if (string.IsNullOrWhiteSpace(jsonOutput))
            return null;

        try
        {
            // Try to parse as JSON first
            using var doc = JsonDocument.Parse(jsonOutput);
            var root = doc.RootElement;

            // Look for result field
            if (root.TryGetProperty("result", out var result))
            {
                return result.GetString();
            }

            // Look for content field
            if (root.TryGetProperty("content", out var content))
            {
                return content.GetString();
            }

            // Return the whole output if it's a string
            if (root.ValueKind == JsonValueKind.String)
            {
                return root.GetString();
            }
        }
        catch (JsonException)
        {
            // Not JSON, return as-is
            return jsonOutput;
        }

        return jsonOutput;
    }

    /// <summary>
    /// Extract clean code from Claude's response, removing markdown fences and conversational text.
    /// </summary>
    private string? ExtractCode(string content, string fileType)
    {
        if (string.IsNullOrWhiteSpace(content))
            return null;

        // Pattern 1: Code in markdown fences
        var fencePattern = fileType switch
        {
            "csharp" => @"```(?:csharp|cs|c#)?\s*\n([\s\S]*?)```",
            "xml" => @"```(?:xml|csproj)?\s*\n([\s\S]*?)```",
            "json" => @"```(?:json)?\s*\n([\s\S]*?)```",
            _ => @"```\w*\s*\n([\s\S]*?)```"
        };

        var match = Regex.Match(content, fencePattern, RegexOptions.IgnoreCase);
        if (match.Success)
        {
            return match.Groups[1].Value.Trim();
        }

        // Pattern 2: Content starts with expected markers
        var cleanContent = content.Trim();

        if (fileType == "csharp" && (cleanContent.StartsWith("using ") || cleanContent.StartsWith("namespace ") ||
            cleanContent.StartsWith("//") || cleanContent.StartsWith("public ") || cleanContent.StartsWith("internal ")))
        {
            return cleanContent;
        }

        if (fileType == "xml" && cleanContent.StartsWith("<"))
        {
            return cleanContent;
        }

        if (fileType == "json" && (cleanContent.StartsWith("{") || cleanContent.StartsWith("[")))
        {
            return cleanContent;
        }

        // Pattern 3: Try to find the code block within conversational response
        if (fileType == "csharp")
        {
            var codeStart = content.IndexOf("using System", StringComparison.Ordinal);
            if (codeStart == -1) codeStart = content.IndexOf("namespace ", StringComparison.Ordinal);
            if (codeStart == -1) codeStart = content.IndexOf("public class", StringComparison.Ordinal);
            if (codeStart == -1) codeStart = content.IndexOf("public record", StringComparison.Ordinal);

            if (codeStart >= 0)
            {
                return content[codeStart..].Trim();
            }
        }

        if (fileType == "xml")
        {
            var xmlStart = content.IndexOf("<Project", StringComparison.Ordinal);
            if (xmlStart == -1) xmlStart = content.IndexOf("<?xml", StringComparison.Ordinal);

            if (xmlStart >= 0)
            {
                return content[xmlStart..].Trim();
            }
        }

        // If nothing worked, return null to trigger a retry
        return null;
    }

    /// <summary>
    /// Parse build errors from dotnet build output.
    /// </summary>
    private List<BuildError> ParseBuildErrors(string buildOutput)
    {
        var errors = new List<BuildError>();
        var pattern = @"([^\s(]+)\((\d+),\d+\):\s*error\s+(\w+):\s*(.+)";

        foreach (Match match in Regex.Matches(buildOutput, pattern))
        {
            errors.Add(new BuildError
            {
                FileName = Path.GetFileName(match.Groups[1].Value),
                Line = int.Parse(match.Groups[2].Value),
                Code = match.Groups[3].Value,
                Message = match.Groups[4].Value.Trim()
            });
        }

        return errors;
    }

    private async Task<(bool Success, string Output)> RunBuildAsync(string directory, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = "build",
            WorkingDirectory = directory,
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

    private async Task<TestResult> RunTestAsync(string directory, string command, CancellationToken ct)
    {
        var result = new TestResult { Command = command };

        try
        {
            var parts = command.Split(' ', 2);
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"run -- {(parts.Length > 1 ? parts[1] : "")}",
                WorkingDirectory = directory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi)!;
            result.Output = await process.StandardOutput.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            result.Success = process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            result.Output = ex.Message;
            result.Success = false;
        }

        return result;
    }

    private string GetDesignPrompt(AppSpec spec) => $@"
Design a {spec.Framework} application: {spec.Name}

Description: {spec.Description}

Requirements:
{string.Join("\n", spec.Requirements.Select(r => $"- {r}"))}

Provide a brief design (5-7 bullet points) covering:
1. Architecture overview
2. Files to create
3. Key classes/types
4. Data flow

Keep it minimal and focused.
";

    private string GetCodeGenerationPrompt(FileSpec file, AppSpec spec, string? designContext, Dictionary<string, string>? generatedFiles = null)
    {
        // Build cross-file context from already-generated files
        var existingFilesContext = "";
        if (generatedFiles?.Count > 0)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("ALREADY GENERATED FILES (use these exact types, methods, and signatures):");
            foreach (var (fileName, code) in generatedFiles)
            {
                sb.AppendLine($"\n--- {fileName} ---");
                sb.AppendLine(code);
                sb.AppendLine($"--- end {fileName} ---");
            }
            existingFilesContext = sb.ToString();
        }

        return $@"
Generate the file: {file.FileName}

Purpose: {file.Purpose}

{(designContext != null ? $"Design context:\n{designContext}\n" : "")}

{existingFilesContext}

Requirements:
- Target framework: {spec.Framework}
- {string.Join("\n- ", file.Requirements)}

CRITICAL IMPLEMENTATION REQUIREMENTS:
- This must be FULLY FUNCTIONAL code, not stubs or placeholders
- All methods must have complete implementations that actually work
- If the code references other files, use the EXACT class names, method signatures, and namespaces from the already-generated files above
- Data persistence means actually reading/writing to files, not just printing messages
- Do not skip any functionality - implement everything specified

IMPORTANT: Output ONLY the code. No explanations, no markdown fences, no commentary.
Start directly with the code (e.g., 'using System;' for C# or '<Project' for csproj).
";
    }

    private string GetFixPrompt(string fileName, string currentCode, string errors) => $@"
Fix the following build errors in {fileName}:

ERRORS:
{errors}

CURRENT CODE:
{currentCode}

IMPORTANT: Output ONLY the corrected code. No explanations, no markdown fences.
Start directly with the code.
";

    private List<FileSpec> GetFilesToGenerate(AppSpec spec)
    {
        // Default files for a console app
        var files = new List<FileSpec>
        {
            new()
            {
                FileName = $"{spec.Name}.csproj",
                FileType = "xml",
                Purpose = "Project configuration",
                Requirements = [$"Target {spec.Framework}", "OutputType Exe", "Nullable enable", "ImplicitUsings enable"]
            }
        };

        // Add spec-defined files
        files.AddRange(spec.Files);

        return files;
    }

    private string GetFileType(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLower();
        return ext switch
        {
            ".cs" => "csharp",
            ".csproj" => "xml",
            ".xml" => "xml",
            ".json" => "json",
            _ => "text"
        };
    }

    private void PrintSummary(AppBuildResult result)
    {
        Console.WriteLine("══════════════════════════════════════════════════════════════");
        Console.WriteLine(result.Success ? "BUILD SUCCEEDED" : "BUILD FAILED");
        Console.WriteLine("══════════════════════════════════════════════════════════════");
        Console.WriteLine();
        Console.WriteLine($"  App:       {result.AppName}");
        Console.WriteLine($"  Output:    {result.OutputDirectory}");
        Console.WriteLine($"  Duration:  {result.Duration.TotalSeconds:F1}s");
        Console.WriteLine($"  Total Cost: ${result.TotalCost:F4}");
        Console.WriteLine($"  Steps:     {result.Steps.Count}");
        Console.WriteLine($"  Retries:   {result.Steps.Sum(s => s.Attempts - 1)}");

        if (!string.IsNullOrEmpty(result.ErrorMessage))
        {
            Console.WriteLine($"  Error:     {result.ErrorMessage}");
        }
        Console.WriteLine();
    }
}

// ============================================================================
// Configuration and Models
// ============================================================================

public class BuilderOptions
{
    public string Model { get; set; } = "sonnet";
    public int MaxTurns { get; set; } = 15;
    public int MaxRetries { get; set; } = 3;
    public int MaxBuildFixAttempts { get; set; } = 3;
}

public class AppSpec
{
    public required string Name { get; set; }
    public required string Description { get; set; }
    public string Framework { get; set; } = "net8.0";
    public required string OutputDirectory { get; set; }
    public List<string> Requirements { get; set; } = [];
    public List<FileSpec> Files { get; set; } = [];
    public string[]? TestCommands { get; set; }
}

public class FileSpec
{
    public required string FileName { get; set; }
    public required string FileType { get; set; }
    public required string Purpose { get; set; }
    public List<string> Requirements { get; set; } = [];
}

public class AppBuildResult
{
    public bool Success { get; set; }
    public bool BuildSuccess { get; set; }
    public string AppName { get; set; } = "";
    public string OutputDirectory { get; set; } = "";
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public TimeSpan Duration => EndTime - StartTime;
    public decimal TotalCost { get; set; }
    public string? ErrorMessage { get; set; }
    public List<BuildStep> Steps { get; set; } = [];
    public List<TestResult> TestResults { get; set; } = [];
}

public class BuildStep
{
    public string Name { get; set; } = "";
    public bool Success { get; set; }
    public int Attempts { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public TimeSpan Duration => EndTime - StartTime;
    public decimal Cost { get; set; }
    public string? Output { get; set; }
    public string? RawOutput { get; set; }
    public string? CleanedCode { get; set; }
    public string? ErrorMessage { get; set; }
}

public class BuildFixResult
{
    public bool Success { get; set; }
    public int Attempts { get; set; }
    public string? ErrorMessage { get; set; }
    public List<BuildStep> Steps { get; set; } = [];
}

public class BuildError
{
    public string FileName { get; set; } = "";
    public int Line { get; set; }
    public string Code { get; set; } = "";
    public string Message { get; set; } = "";
}

public class TestResult
{
    public string Command { get; set; } = "";
    public bool Success { get; set; }
    public string? Output { get; set; }
}
