# build-todo-app-copilot.ps1
# Build a Todo CLI app using ONLY GitHub Copilot (Chat Completions API)
# This tests the new API-first GitHubCopilotProvider with self-healing builds.
#
# Prerequisites:
#   - GITHUB_TOKEN env var set (GitHub Pro with Copilot access)
#   - .NET SDK installed
#
# Usage: .\build-todo-app-copilot.ps1 [-OutputDir "C:\path\to\output"] [-MaxRetries 3]

param(
    [string]$OutputDir = ".\generated\TodoCli-Copilot",
    [int]$MaxRetries = 3,
    [string]$Model = "openai/gpt-4o"
)

$ErrorActionPreference = "Continue"
$BuildSuccess = $false
$ApiBase = "https://models.github.ai/inference/chat/completions"
$TotalPrompts = 0
$TotalTokens = 0

# ============================================================================
# Helper: Call the GitHub Copilot Chat Completions API directly
# ============================================================================
function Invoke-CopilotApi {
    param(
        [string]$SystemPrompt,
        [string]$UserPrompt,
        [string]$ModelName = $Model,
        [int]$MaxTokens = 4096,
        [double]$Temperature = 0.1
    )

    $token = $env:GITHUB_TOKEN
    if (-not $token) { $token = $env:GH_TOKEN }
    if (-not $token) {
        Write-Host "  [ERROR] GITHUB_TOKEN not set." -ForegroundColor Red
        return $null
    }

    $body = @{
        model    = $ModelName
        messages = @(
            @{ role = "system"; content = $SystemPrompt }
            @{ role = "user";   content = $UserPrompt }
        )
        max_tokens  = $MaxTokens
        temperature = $Temperature
    } | ConvertTo-Json -Depth 10

    $headers = @{
        "Authorization" = "Bearer $token"
        "Content-Type"  = "application/json"
        "Accept"        = "application/vnd.github+json"
        "X-GitHub-Api-Version" = "2022-11-28"
        "User-Agent"    = "dotnet-automation/1.0"
    }

    $maxApiRetries = 3
    for ($r = 1; $r -le $maxApiRetries; $r++) {
        try {
            $response = Invoke-RestMethod -Uri $ApiBase -Method Post -Headers $headers -Body $body -TimeoutSec 120
            $script:TotalPrompts++
            if ($response.usage) {
                $script:TotalTokens += ($response.usage.total_tokens ?? 0)
            }
            return $response.choices[0].message.content
        }
        catch {
            $statusCode = $_.Exception.Response.StatusCode.value__
            if ($statusCode -eq 429 -and $r -lt $maxApiRetries) {
                $wait = [math]::Pow(2, $r) * 5
                Write-Host "  [RATE-LIMITED] Waiting ${wait}s before retry $r/$maxApiRetries..." -ForegroundColor Yellow
                Start-Sleep -Seconds $wait
            }
            elseif ($statusCode -ge 500 -and $r -lt $maxApiRetries) {
                Write-Host "  [SERVER ERROR] Retry $r/$maxApiRetries..." -ForegroundColor Yellow
                Start-Sleep -Seconds 3
            }
            else {
                Write-Host "  [API ERROR] $($_.Exception.Message)" -ForegroundColor Red
                return $null
            }
        }
    }
    return $null
}

# ============================================================================
# Helper: Extract clean code from API response
# ============================================================================
function Extract-Code {
    param([string]$Response, [string]$Language = "")

    $code = $Response

    # Remove markdown code fences
    if ($code -match '(?s)```(?:\w+)?\s*\n(.*?)\n```') {
        $code = $Matches[1]
    }

    # Remove conversational prefixes
    $patterns = @(
        "^Here's the .*?:\s*\n",
        "^Here is the .*?:\s*\n",
        "^Below is .*?:\s*\n",
        "^The following .*?:\s*\n",
        "^I've created .*?:\s*\n",
        "^Sure.*?:\s*\n"
    )
    foreach ($pattern in $patterns) {
        $code = $code -replace $pattern, ""
    }

    # For XML, ensure we start at the right place
    if ($Language -eq "xml") {
        if ($code -match '(<\?xml[\s\S]*|<Project[\s\S]*)') {
            $code = $Matches[1]
        }
    }

    # For C#, start from first valid statement
    if ($Language -eq "csharp") {
        $lines = $code -split "`n"
        $validLines = @()
        $inCode = $false
        foreach ($line in $lines) {
            if ($line -match '^\s*(using|namespace|public|internal|class|record|enum|//|#|global)' -or $inCode) {
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

# ============================================================================
# System prompt for autonomous coding
# ============================================================================
$CodingSystemPrompt = @"
You are an expert C# developer acting as an autonomous coding agent.
You generate clean, compilable, production-quality C# code.

Rules:
- Output ONLY the raw code for the requested file. No markdown fences, no explanations, no preamble.
- Start directly with 'using' statements or XML declarations.
- Use modern C# features (top-level statements, records, pattern matching).
- All code MUST compile without errors on .NET 8+.
- Use System.Text.Json for serialization (no external NuGet packages).
- If fixing errors, return the COMPLETE corrected file, not a partial diff.
"@

# ============================================================================
# Main Script
# ============================================================================
Write-Host ""
Write-Host "======================================================================" -ForegroundColor Cyan
Write-Host "   Build Todo App - GitHub Copilot Only (API)                        " -ForegroundColor Cyan
Write-Host "   Model: $Model | Self-Healing: Up to $MaxRetries attempts          " -ForegroundColor Cyan
Write-Host "======================================================================" -ForegroundColor Cyan
Write-Host ""

# Check prerequisites
Write-Host "Checking prerequisites..." -ForegroundColor Yellow

# Check GITHUB_TOKEN
$ghToken = $env:GITHUB_TOKEN
if (-not $ghToken) { $ghToken = $env:GH_TOKEN }
if ($ghToken) {
    $masked = $ghToken.Substring(0, [math]::Min(8, $ghToken.Length)) + "..."
    Write-Host "  [OK] GITHUB_TOKEN: $masked" -ForegroundColor Green
} else {
    Write-Host "  [ERROR] GITHUB_TOKEN not set." -ForegroundColor Red
    Write-Host "         Set with: `$env:GITHUB_TOKEN = 'ghp_your-token'" -ForegroundColor Yellow
    Write-Host "         Create a fine-grained PAT with 'models:read' scope" -ForegroundColor Yellow
    exit 1
}

# Check .NET
$dotnetVersion = dotnet --version 2>&1
if ($LASTEXITCODE -eq 0) {
    Write-Host "  [OK] .NET SDK: $dotnetVersion" -ForegroundColor Green
} else {
    Write-Host "  [ERROR] .NET SDK not found." -ForegroundColor Red
    exit 1
}

# Quick API connectivity check
Write-Host "  Checking Copilot API connectivity..." -ForegroundColor Gray
$testResult = Invoke-CopilotApi -SystemPrompt "Reply with only: OK" -UserPrompt "Health check" -MaxTokens 10
if ($testResult) {
    Write-Host "  [OK] Copilot API: Connected ($Model)" -ForegroundColor Green
} else {
    Write-Host "  [ERROR] Cannot reach Copilot API. Check your token and subscription." -ForegroundColor Red
    exit 1
}

# Create output directory
Write-Host ""
Write-Host "Creating output directory: $OutputDir" -ForegroundColor Yellow
if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
}
$OutputDir = Resolve-Path $OutputDir

$timer = [Diagnostics.Stopwatch]::StartNew()

# ── Step 1: Design ──────────────────────────────────────────────────────────
Write-Host ""
Write-Host "STEP 1: Designing application architecture..." -ForegroundColor Cyan
Write-Host "─────────────────────────────────────────────" -ForegroundColor DarkGray

$designPrompt = @"
Design a simple C# command-line Todo application.

Commands:
- todo add "task" [--priority high|medium|low]
- todo list [--all | --pending | --completed]
- todo complete <id>
- todo delete <id>

Requirements:
- Store todos in a local JSON file (todos.json)
- Priority levels: Low, Medium, High
- Show completion status with checkmarks

Provide a brief 3-5 bullet point design with the file names and their purpose.
"@

$design = Invoke-CopilotApi -SystemPrompt "You are a software architect. Be concise." -UserPrompt $designPrompt -MaxTokens 500
if (-not $design) {
    Write-Host "  [ERROR] Design step failed" -ForegroundColor Red
    exit 1
}
Write-Host $design -ForegroundColor White
Write-Host "  [OK] Design complete" -ForegroundColor Green

# ── Step 2: Generate Program.cs ─────────────────────────────────────────────
Write-Host ""
Write-Host "STEP 2: Generating Program.cs..." -ForegroundColor Cyan
Write-Host "─────────────────────────────────────────────" -ForegroundColor DarkGray

$programPrompt = @"
Generate a complete C# Program.cs for a Todo CLI app.

Commands:
- todo add "task" [--priority high|medium|low]
- todo list [--all|--pending|--completed]
- todo complete <id>
- todo delete <id>

Requirements:
- Top-level statements (no namespace, no Main method wrapper)
- Manual argument parsing (no external libraries)
- Store in todos.json in current directory
- Clean, user-friendly console output with Unicode checkmarks
- Reference TodoItem record, Priority enum, and TodoStore class from TodoItem.cs

Start with 'using' statements. Output ONLY the C# code.
"@

Write-Host "Calling Copilot API ($Model)..." -ForegroundColor Gray
$programCode = Invoke-CopilotApi -SystemPrompt $CodingSystemPrompt -UserPrompt $programPrompt
if (-not $programCode) {
    Write-Host "  [ERROR] Code generation failed" -ForegroundColor Red
    exit 1
}
$programCode = Extract-Code -Response $programCode -Language "csharp"
$programPath = Join-Path $OutputDir "Program.cs"
$programCode | Out-File -FilePath $programPath -Encoding UTF8
$lineCount = ($programCode -split "`n").Count
Write-Host "  [OK] Program.cs generated ($lineCount lines)" -ForegroundColor Green

# ── Step 3: Generate TodoItem.cs ─────────────────────────────────────────────
Write-Host ""
Write-Host "STEP 3: Generating TodoItem.cs (models)..." -ForegroundColor Cyan
Write-Host "─────────────────────────────────────────────" -ForegroundColor DarkGray

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
   - void Save(string path, List<TodoItem> items) - writes JSON with WriteIndented = true
   - int GetNextId(List<TodoItem> items) - returns max Id + 1, or 1 if empty

Use System.Text.Json with JsonSerializerOptions { WriteIndented = true }.
Use System.Text.Json.Serialization for JsonStringEnumConverter attribute on Priority enum.
Output ONLY the C# code. Start with 'using' statements.
"@

Write-Host "Calling Copilot API ($Model)..." -ForegroundColor Gray
$modelsCode = Invoke-CopilotApi -SystemPrompt $CodingSystemPrompt -UserPrompt $modelsPrompt
if (-not $modelsCode) {
    Write-Host "  [ERROR] Models generation failed" -ForegroundColor Red
    exit 1
}
$modelsCode = Extract-Code -Response $modelsCode -Language "csharp"
$modelsPath = Join-Path $OutputDir "TodoItem.cs"
$modelsCode | Out-File -FilePath $modelsPath -Encoding UTF8
$lineCount = ($modelsCode -split "`n").Count
Write-Host "  [OK] TodoItem.cs generated ($lineCount lines)" -ForegroundColor Green

# ── Step 4: Generate .csproj ─────────────────────────────────────────────────
Write-Host ""
Write-Host "STEP 4: Generating project file..." -ForegroundColor Cyan
Write-Host "─────────────────────────────────────────────" -ForegroundColor DarkGray

$csprojPrompt = @"
Generate a minimal .csproj file for a .NET 8 console application called TodoCli.

Requirements:
- OutputType: Exe
- TargetFramework: net8.0
- Nullable: enable
- ImplicitUsings: enable
- No external NuGet packages needed

Output ONLY the XML. Start with <Project.
"@

Write-Host "Calling Copilot API ($Model)..." -ForegroundColor Gray
$csprojCode = Invoke-CopilotApi -SystemPrompt $CodingSystemPrompt -UserPrompt $csprojPrompt -MaxTokens 500
if (-not $csprojCode) {
    Write-Host "  [ERROR] Project file generation failed" -ForegroundColor Red
    exit 1
}
$csprojCode = Extract-Code -Response $csprojCode -Language "xml"
$csprojPath = Join-Path $OutputDir "TodoCli.csproj"
$csprojCode | Out-File -FilePath $csprojPath -Encoding UTF8
Write-Host "  [OK] TodoCli.csproj generated" -ForegroundColor Green

# ── Step 5: Build with self-healing ──────────────────────────────────────────
Write-Host ""
Write-Host "STEP 5: Building with self-healing..." -ForegroundColor Cyan
Write-Host "─────────────────────────────────────────────" -ForegroundColor DarkGray

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
        Write-Host "  [FAIL] Build errors:" -ForegroundColor Red
        # Show just the error lines, not the full MSBuild output
        $buildOutput -split "`n" | Where-Object { $_ -match "error CS|error MSB" } | ForEach-Object {
            Write-Host "    $_" -ForegroundColor Gray
        }

        if ($attempt -lt $MaxRetries) {
            Write-Host ""
            Write-Host "  Sending errors back to Copilot for auto-fix..." -ForegroundColor Yellow

            # Determine which file(s) have errors
            $filesToFix = @()
            if ($buildOutput -match "Program\.cs") { $filesToFix += "Program.cs" }
            if ($buildOutput -match "TodoItem\.cs") { $filesToFix += "TodoItem.cs" }
            if ($buildOutput -match "\.csproj")     { $filesToFix += "TodoCli.csproj" }

            # If we can't tell, fix all C# files
            if ($filesToFix.Count -eq 0) { $filesToFix = @("Program.cs", "TodoItem.cs") }

            foreach ($errorFile in $filesToFix) {
                $filePath = Join-Path $OutputDir $errorFile
                if (-not (Test-Path $filePath)) { continue }

                $currentCode = Get-Content $filePath -Raw

                # Also include the other file for cross-reference context
                $otherContext = ""
                if ($errorFile -eq "Program.cs" -and (Test-Path (Join-Path $OutputDir "TodoItem.cs"))) {
                    $otherContext = "`n`nFor reference, here is TodoItem.cs:`n" + (Get-Content (Join-Path $OutputDir "TodoItem.cs") -Raw)
                }
                elseif ($errorFile -eq "TodoItem.cs" -and (Test-Path (Join-Path $OutputDir "Program.cs"))) {
                    $otherContext = "`n`nFor reference, here is Program.cs:`n" + (Get-Content (Join-Path $OutputDir "Program.cs") -Raw)
                }

                $fixPrompt = @"
The following C# code has compilation errors. Fix ALL errors and return the COMPLETE corrected file.

FILE: $errorFile

BUILD ERRORS:
$buildOutput
$otherContext

CURRENT CODE:
$currentCode

Return the COMPLETE fixed file. Output ONLY the corrected code.
"@

                Write-Host "  Fixing $errorFile..." -ForegroundColor Gray
                $lang = if ($errorFile -match "\.csproj") { "xml" } else { "csharp" }
                $fixedCode = Invoke-CopilotApi -SystemPrompt $CodingSystemPrompt -UserPrompt $fixPrompt
                if ($fixedCode) {
                    $fixedCode = Extract-Code -Response $fixedCode -Language $lang
                    $fixedCode | Out-File -FilePath $filePath -Encoding UTF8
                    Write-Host "  [OK] Applied fix to $errorFile" -ForegroundColor Green
                } else {
                    Write-Host "  [WARN] Fix attempt failed for $errorFile" -ForegroundColor Yellow
                }
            }
        }
    }
}

Pop-Location
$timer.Stop()

# ── Summary ──────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "======================================================================" -ForegroundColor Cyan

if ($BuildSuccess) {
    Write-Host "  BUILD SUCCESSFUL!" -ForegroundColor Green
    Write-Host "======================================================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "  Provider:   GitHub Copilot API ($Model)" -ForegroundColor White
    Write-Host "  Prompts:    $TotalPrompts" -ForegroundColor White
    Write-Host "  Tokens:     $TotalTokens" -ForegroundColor White
    Write-Host "  Duration:   $($timer.Elapsed.TotalSeconds.ToString('F1'))s" -ForegroundColor White
    Write-Host "  Attempts:   $attempt" -ForegroundColor White
    Write-Host ""
    Write-Host "  Generated files:" -ForegroundColor Yellow
    Get-ChildItem $OutputDir -File | ForEach-Object {
        Write-Host "    - $($_.Name) ($($_.Length) bytes)" -ForegroundColor Gray
    }
    Write-Host ""
    Write-Host "  Try it out:" -ForegroundColor Yellow
    Write-Host "    cd $OutputDir" -ForegroundColor White
    Write-Host '    dotnet run -- add "Buy groceries" --priority high' -ForegroundColor White
    Write-Host '    dotnet run -- add "Walk the dog" --priority medium' -ForegroundColor White
    Write-Host '    dotnet run -- list' -ForegroundColor White
    Write-Host '    dotnet run -- complete 1' -ForegroundColor White
    Write-Host '    dotnet run -- list --all' -ForegroundColor White
} else {
    Write-Host "  BUILD FAILED after $MaxRetries attempts" -ForegroundColor Red
    Write-Host "======================================================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "  Provider:   GitHub Copilot API ($Model)" -ForegroundColor White
    Write-Host "  Prompts:    $TotalPrompts" -ForegroundColor White
    Write-Host ""
    Write-Host "  Generated files:" -ForegroundColor Yellow
    Get-ChildItem $OutputDir -File | ForEach-Object {
        Write-Host "    - $($_.Name) ($($_.Length) bytes)" -ForegroundColor Gray
    }
    Write-Host ""
    Write-Host "  Review the errors manually, or run again." -ForegroundColor Yellow
    exit 1
}

Write-Host ""
