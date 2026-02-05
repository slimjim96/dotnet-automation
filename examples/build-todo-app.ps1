# build-todo-app.ps1
# Windows PowerShell script to build a Todo app using Claude Code automation
#
# Usage: .\build-todo-app.ps1 [-OutputDir "C:\path\to\output"]

param(
    [string]$OutputDir = ".\generated\TodoCli"
)

$ErrorActionPreference = "Continue"

Write-Host ""
Write-Host "======================================================================" -ForegroundColor Cyan
Write-Host "   Build Todo App with Claude Code Automation (Windows)              " -ForegroundColor Cyan
Write-Host "======================================================================" -ForegroundColor Cyan
Write-Host ""

# Check prerequisites
Write-Host "Checking prerequisites..." -ForegroundColor Yellow

# Check Claude CLI
$claudeVersion = claude --version 2>&1
if ($LASTEXITCODE -eq 0) {
    Write-Host "  [OK] Claude CLI: $claudeVersion" -ForegroundColor Green
} else {
    Write-Host "  [ERROR] Claude CLI not found. Install with: npm install -g @anthropic/claude-code" -ForegroundColor Red
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
$design = claude --print $designPrompt --model haiku 2>&1
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
- Top-level statements
- Manual argument parsing (no external libs)
- Store in todos.json in current directory
- Clean, user-friendly output
- Reference TodoItem and TodoStore from TodoItem.cs

Output ONLY valid C# code, no markdown code fences or explanations.
"@

Write-Host "Generating code with Claude (sonnet)..." -ForegroundColor Gray
$programCode = claude --print $programPrompt --model sonnet 2>&1
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
   - List<TodoItem> Load(string path)
   - void Save(string path, List<TodoItem> items)
   - int GetNextId(List<TodoItem> items)

Use System.Text.Json with options for pretty printing.
Output ONLY valid C# code, no markdown code fences or explanations.
"@

Write-Host "Generating models with Claude (sonnet)..." -ForegroundColor Gray
$modelsCode = claude --print $modelsPrompt --model sonnet 2>&1
$modelsPath = Join-Path $OutputDir "TodoItem.cs"
$modelsCode | Out-File -FilePath $modelsPath -Encoding UTF8
$lineCount = (Get-Content $modelsPath | Measure-Object -Line).Lines
Write-Host "  [OK] TodoItem.cs generated ($lineCount lines)" -ForegroundColor Green

# Step 4: Generate .csproj
Write-Host ""
Write-Host "STEP 4: Generating project file..." -ForegroundColor Cyan
Write-Host "---------------------------------------------" -ForegroundColor DarkGray

$csprojPrompt = @"
Generate a .csproj file for a .NET 8 console application:
- Project name: TodoCli
- OutputType: Exe
- TargetFramework: net8.0
- Nullable: enable
- ImplicitUsings: enable
- No NuGet packages needed

Output ONLY valid XML, no markdown code fences or explanations.
"@

Write-Host "Generating project file with Claude (haiku)..." -ForegroundColor Gray
$csprojCode = claude --print $csprojPrompt --model haiku 2>&1
$csprojPath = Join-Path $OutputDir "TodoCli.csproj"
$csprojCode | Out-File -FilePath $csprojPath -Encoding UTF8
Write-Host "  [OK] TodoCli.csproj generated" -ForegroundColor Green

# Step 5: Build the project
Write-Host ""
Write-Host "STEP 5: Building the generated application..." -ForegroundColor Cyan
Write-Host "---------------------------------------------" -ForegroundColor DarkGray

Push-Location $OutputDir
$buildOutput = dotnet build 2>&1
if ($LASTEXITCODE -eq 0) {
    Write-Host "  [OK] Build successful!" -ForegroundColor Green
} else {
    Write-Host "  [WARN] Build had errors:" -ForegroundColor Yellow
    Write-Host $buildOutput -ForegroundColor Gray
}
Pop-Location

# Return to original location
Pop-Location

# Summary
Write-Host ""
Write-Host "======================================================================" -ForegroundColor Cyan
Write-Host "BUILD COMPLETE!" -ForegroundColor Green
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
Write-Host ""
