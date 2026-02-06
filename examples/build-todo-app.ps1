# build-todo-app.ps1
# Windows PowerShell script to build a Todo app using Claude Code automation
# Features: Self-healing builds with automatic error correction
#
# Usage: .\build-todo-app.ps1 [-OutputDir "C:\path\to\output"]

param(
    [string]$OutputDir = ".\generated\TodoCli",
    [int]$MaxRetries = 3
)

$ErrorActionPreference = "Continue"
$BuildSuccess = $false

# Helper function to extract clean code from Claude's response
function Extract-Code {
    param([string]$Response, [string]$Language = "")

    $code = $Response

    # Remove markdown code fences
    if ($code -match '```(?:\w+)?\s*\n([\s\S]*?)\n```') {
        $code = $Matches[1]
    }

    # Remove common conversational prefixes
    $patterns = @(
        "^Here's the .*?:\s*\n",
        "^Here is the .*?:\s*\n",
        "^Below is .*?:\s*\n",
        "^The following .*?:\s*\n",
        "^I've created .*?:\s*\n"
    )
    foreach ($pattern in $patterns) {
        $code = $code -replace $pattern, ""
    }

    # For XML, ensure we start with <?xml or <Project
    if ($Language -eq "xml") {
        if ($code -match '(<\?xml[\s\S]*|<Project[\s\S]*)') {
            $code = $Matches[1]
        }
    }

    # For C#, ensure we have valid code structure
    if ($Language -eq "csharp") {
        # Remove any trailing explanation
        $lines = $code -split "`n"
        $validLines = @()
        $inCode = $false
        foreach ($line in $lines) {
            if ($line -match '^\s*(using|namespace|public|internal|class|record|enum|//)' -or $inCode) {
                $inCode = $true
                $validLines += $line
            }
        }
        if ($validLines.Count -gt 0) {
            $code = $validLines -join "`n"
        }
    }

    return $code.Trim()
}

# Helper function to run Claude and get clean output
function Invoke-Claude {
    param(
        [string]$Prompt,
        [string]$Model = "sonnet",
        [string]$Language = ""
    )

    $response = claude --print $Prompt --model $Model --dangerously-skip-permissions 2>&1
    return Extract-Code -Response ($response | Out-String) -Language $Language
}

Write-Host ""
Write-Host "======================================================================" -ForegroundColor Cyan
Write-Host "   Build Todo App with Claude Code Automation (Windows)              " -ForegroundColor Cyan
Write-Host "   Self-Healing Mode: Up to $MaxRetries fix attempts                 " -ForegroundColor Cyan
Write-Host "======================================================================" -ForegroundColor Cyan
Write-Host ""

# Check prerequisites
Write-Host "Checking prerequisites..." -ForegroundColor Yellow

# Check Claude CLI
$claudeVersion = claude --version 2>&1
if ($LASTEXITCODE -eq 0) {
    Write-Host "  [OK] Claude CLI: $claudeVersion" -ForegroundColor Green
} else {
    Write-Host "  [ERROR] Claude CLI not found." -ForegroundColor Red
    Write-Host "         Install with: irm https://claude.ai/install.ps1 | iex" -ForegroundColor Yellow
    exit 1
}

# Check .NET
$dotnetVersion = dotnet --version 2>&1
if ($LASTEXITCODE -eq 0) {
    Write-Host "  [OK] .NET SDK: $dotnetVersion" -ForegroundColor Green
} else {
    Write-Host "  [ERROR] .NET SDK not found. Install from https://dotnet.microsoft.com" -ForegroundColor Red
    exit 1
}

# Create output directory
Write-Host ""
Write-Host "Creating output directory: $OutputDir" -ForegroundColor Yellow
if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
}
$OutputDir = Resolve-Path $OutputDir

# Navigate to the project directory
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = Split-Path -Parent $ScriptDir
Push-Location $ProjectRoot

# Step 1: Design
Write-Host ""
Write-Host "STEP 1: Designing application architecture..." -ForegroundColor Cyan
Write-Host "---------------------------------------------" -ForegroundColor DarkGray

$designPrompt = "Design a simple C# command-line Todo application with add, list, complete, delete commands. Store todos in JSON. Include priority levels. Keep it minimal. Provide a brief 3-5 bullet point design."

Write-Host "Running design prompt..." -ForegroundColor Gray
$design = Invoke-Claude -Prompt $designPrompt -Model "haiku"
Write-Host $design -ForegroundColor White
Write-Host ""
Write-Host "  [OK] Design complete" -ForegroundColor Green

# Step 2: Generate Program.cs
Write-Host ""
Write-Host "STEP 2: Generating Program.cs..." -ForegroundColor Cyan
Write-Host "---------------------------------------------" -ForegroundColor DarkGray

$programPrompt = @"
Generate a complete C# Program.cs for a Todo CLI app.

Commands:
- todo add "task" [--priority high|medium|low]
- todo list [--all|--pending|--completed]
- todo complete <id>
- todo delete <id>

Requirements:
- Top-level statements (no namespace, no class wrapper for Main)
- Manual argument parsing (no external libs)
- Store in todos.json in current directory
- Clean, user-friendly output
- Reference TodoItem and TodoStore from TodoItem.cs

IMPORTANT: Output ONLY the raw C# code. No markdown fences, no explanations, no "Here's the code" prefix. Start directly with 'using' statements.
"@

Write-Host "Generating code with Claude (sonnet)..." -ForegroundColor Gray
$programCode = Invoke-Claude -Prompt $programPrompt -Model "sonnet" -Language "csharp"
$programPath = Join-Path $OutputDir "Program.cs"
$programCode | Out-File -FilePath $programPath -Encoding UTF8
$lineCount = (Get-Content $programPath | Measure-Object -Line).Lines
Write-Host "  [OK] Program.cs generated ($lineCount lines)" -ForegroundColor Green

# Step 3: Generate TodoItem.cs
Write-Host ""
Write-Host "STEP 3: Generating TodoItem.cs (models)..." -ForegroundColor Cyan
Write-Host "---------------------------------------------" -ForegroundColor DarkGray

$modelsPrompt = @"
Generate a C# file TodoItem.cs containing:

1. Priority enum: Low = 0, Medium = 1, High = 2
2. TodoItem record with properties:
   - int Id
   - string Title
   - bool IsCompleted
   - Priority Priority
   - DateTime CreatedAt
3. TodoStore class with static methods:
   - List<TodoItem> Load(string path) - reads JSON, returns empty list if file missing
   - void Save(string path, List<TodoItem> items) - writes JSON with indentation
   - int GetNextId(List<TodoItem> items) - returns max Id + 1, or 1 if empty

Use System.Text.Json with WriteIndented = true.
IMPORTANT: Output ONLY the raw C# code. No markdown fences, no explanations. Start directly with 'using' statements.
"@

Write-Host "Generating models with Claude (sonnet)..." -ForegroundColor Gray
$modelsCode = Invoke-Claude -Prompt $modelsPrompt -Model "sonnet" -Language "csharp"
$modelsPath = Join-Path $OutputDir "TodoItem.cs"
$modelsCode | Out-File -FilePath $modelsPath -Encoding UTF8
$lineCount = (Get-Content $modelsPath | Measure-Object -Line).Lines
Write-Host "  [OK] TodoItem.cs generated ($lineCount lines)" -ForegroundColor Green

# Step 4: Generate .csproj
Write-Host ""
Write-Host "STEP 4: Generating project file..." -ForegroundColor Cyan
Write-Host "---------------------------------------------" -ForegroundColor DarkGray

$csprojPrompt = @"
Generate a .csproj file for a .NET 8 console application.

Requirements:
- OutputType: Exe
- TargetFramework: net8.0
- Nullable: enable
- ImplicitUsings: enable
- No NuGet packages needed

IMPORTANT: Output ONLY the raw XML. No markdown fences, no explanations. Start directly with <Project or <?xml.
"@

Write-Host "Generating project file with Claude (haiku)..." -ForegroundColor Gray
$csprojCode = Invoke-Claude -Prompt $csprojPrompt -Model "haiku" -Language "xml"
$csprojPath = Join-Path $OutputDir "TodoCli.csproj"
$csprojCode | Out-File -FilePath $csprojPath -Encoding UTF8
Write-Host "  [OK] TodoCli.csproj generated" -ForegroundColor Green

# Step 5: Build with self-healing
Write-Host ""
Write-Host "STEP 5: Building the generated application..." -ForegroundColor Cyan
Write-Host "---------------------------------------------" -ForegroundColor DarkGray

Push-Location $OutputDir

$attempt = 0
while ($attempt -lt $MaxRetries -and -not $BuildSuccess) {
    $attempt++
    Write-Host ""
    Write-Host "  Build attempt $attempt of $MaxRetries..." -ForegroundColor Yellow

    $buildOutput = dotnet build 2>&1 | Out-String

    if ($LASTEXITCODE -eq 0) {
        Write-Host "  [OK] Build successful!" -ForegroundColor Green
        $BuildSuccess = $true
    } else {
        Write-Host "  [ERROR] Build failed:" -ForegroundColor Red
        Write-Host $buildOutput -ForegroundColor Gray

        if ($attempt -lt $MaxRetries) {
            Write-Host ""
            Write-Host "  Sending errors to Claude for auto-fix..." -ForegroundColor Yellow

            # Determine which file has the error
            $errorFile = ""
            if ($buildOutput -match "Program\.cs") { $errorFile = "Program.cs" }
            elseif ($buildOutput -match "TodoItem\.cs") { $errorFile = "TodoItem.cs" }
            elseif ($buildOutput -match "\.csproj") { $errorFile = "TodoCli.csproj" }

            if ($errorFile -ne "") {
                $filePath = Join-Path $OutputDir $errorFile
                $currentCode = Get-Content $filePath -Raw

                $fixPrompt = @"
The following C# code has compilation errors. Fix ALL the errors and return the COMPLETE corrected file.

ERRORS:
$buildOutput

CURRENT CODE:
$currentCode

IMPORTANT: Output ONLY the corrected code. No markdown fences, no explanations. The code must compile without errors.
"@

                Write-Host "  Fixing $errorFile..." -ForegroundColor Gray
                $lang = if ($errorFile -match "\.csproj") { "xml" } else { "csharp" }
                $fixedCode = Invoke-Claude -Prompt $fixPrompt -Model "sonnet" -Language $lang
                $fixedCode | Out-File -FilePath $filePath -Encoding UTF8
                Write-Host "  [OK] Applied fix to $errorFile" -ForegroundColor Green
            } else {
                Write-Host "  [WARN] Could not determine which file to fix" -ForegroundColor Yellow
            }
        }
    }
}

Pop-Location

# Return to original location
Pop-Location

# Summary
Write-Host ""
Write-Host "======================================================================" -ForegroundColor Cyan
if ($BuildSuccess) {
    Write-Host "BUILD SUCCESSFUL!" -ForegroundColor Green
    Write-Host "======================================================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Generated files in: $OutputDir" -ForegroundColor Yellow
    Get-ChildItem $OutputDir -File | ForEach-Object {
        Write-Host "  - $($_.Name) ($($_.Length) bytes)" -ForegroundColor Gray
    }
    Write-Host ""
    Write-Host "To run the app:" -ForegroundColor Yellow
    Write-Host "  cd $OutputDir" -ForegroundColor White
    Write-Host '  dotnet run -- add "My first task" --priority high' -ForegroundColor White
    Write-Host '  dotnet run -- list' -ForegroundColor White
    Write-Host '  dotnet run -- complete 1' -ForegroundColor White
} else {
    Write-Host "BUILD FAILED after $MaxRetries attempts" -ForegroundColor Red
    Write-Host "======================================================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Generated files in: $OutputDir" -ForegroundColor Yellow
    Get-ChildItem $OutputDir -File | ForEach-Object {
        Write-Host "  - $($_.Name) ($($_.Length) bytes)" -ForegroundColor Gray
    }
    Write-Host ""
    Write-Host "Try manually reviewing and fixing the errors, or run again." -ForegroundColor Yellow
    exit 1
}
Write-Host ""
