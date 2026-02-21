# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

A .NET 10 automation framework that wraps the Claude Code CLI as a subprocess (`Process.Start()`) for programmatic AI task execution. No daemon or intermediate server required. The project name is "HumanLayerAutomation" (legacy naming from a previous daemon-based architecture).

## Build & Run Commands

```bash
# Restore and build
dotnet restore
dotnet build

# Run from the HumanLayerAutomation directory
cd HumanLayerAutomation
dotnet run -- demo                          # Quick demo
dotnet run -- run "your prompt"             # Single task
dotnet run -- scheduler                     # Cron-like scheduled tasks
dotnet run -- parallel                      # Concurrent task execution
dotnet run -- stream                        # Streaming output demo
dotnet run -- auto new "description" ./path # Autonomous builder
dotnet run -- run-config config.json        # Run from JSON config
dotnet run -- status                        # System status overview
dotnet run -- health                        # Full health check
```

There are no tests in this project.

## Architecture

### Core Layer
- **`ClaudeCodeClient`** (`ClaudeCodeClient.cs`) - Central class. Wraps Claude CLI via `System.Diagnostics.Process`. Builds CLI arguments (`--print`, `--model`, `--output-format json`, `--dangerously-skip-permissions`, etc.), parses JSON results. Supports `RunAsync` (batch), `RunStreamingAsync` (real-time output), `RunTaskAsync` (high-level), and `ContinueSessionAsync` (session resume). Implements `IDisposable` to kill child processes.
- **`Models.cs`** - All data models in one file: `ClaudeOptions`, `ClaudeResult`, `ClaudeJsonResult`, `AutomationTask`, `TaskResult`, `ScheduledTask`, `ClaudeStreamEvent`, `ToolPermissionRequest`, `ToolRiskLevel` enum, `ToolClassification` (static risk categorization of Claude tools).

### Builder Layer (two builders)
- **`AutomatedAppBuilder`** (`AutomatedAppBuilder.cs`) - Spec-driven builder with self-healing. Takes an `AppSpec` (explicit file list with requirements), generates code in phases (Design → Generate per file → Build → Fix), retries on errors by feeding build output back to Claude.
- **`AutoBuilder`** (`AutoBuilder.cs`) - Scenario-based autonomous builder. Uses `BuildConfig` with scenario (new/update/add), strategy (first/efficient/thorough/balanced), and scope (component/core/full). More flexible, single-prompt approach.
- **`BuildConfig.cs`** - Enums (`BuildScenario`, `DecisionStrategy`, `BuildScope`) and configuration for `AutoBuilder`.
- **`TaskConfig.cs`** - JSON-serializable task configuration with `TaskConfigLoader` for file-based build configs and `TaskRegistry` for tracking build status.

### Multi-Provider System (`Providers/`)
- **`ICodeProvider`** - Interface for AI providers: `IsAvailableAsync`, `RunAsync`, `GetQuotaAsync`.
- **`ClaudeProvider`** - Wraps `ClaudeCodeClient` as an `ICodeProvider`.
- **`GitHubCopilotProvider`** / **`GitHubCopilotClient`** - GitHub Copilot integration via `gh copilot` CLI.
- **`OpenAIProvider`** - OpenAI API via direct HTTP calls.
- **`MultiProvider`** - Orchestrates providers with automatic fallback and quota-aware routing.

### Supporting Systems
- **`Models/ModelRegistry.cs`** - Model catalog with pricing, aliases, capabilities. Persists to `%APPDATA%/dotnet-automation/model-registry.json`. Tracks per-model usage/cost.
- **`Models/QuotaManager.cs`** - Per-provider quota tracking with billing models: time-based reset, monthly limit, pay-per-use. Persists to `%APPDATA%/dotnet-automation/provider-quotas.json`.
- **`Notifications/`** - `NotificationManager` with channels: `EmailChannel` (SMTP), `DiscordChannel` (webhook), `NtfyChannel` (push notifications).
- **`Health/`** - `HealthCheckService` checks CLI availability and auth for Claude/GitHub/OpenAI. `SystemStatus` gives consolidated view. `SetupWizard` for interactive configuration.

### Entry Point
- **`Program.cs`** - Top-level statements with a switch on `args[0]` dispatching to mode handlers. All CLI commands are defined here.

## Key Patterns

- Configuration is primarily via environment variables (`CLAUDE_PATH`, `CLAUDE_MODEL`, `MAX_TURNS`, `AUTO_APPROVE`, `WORKING_DIR`, `OUTPUT_DIR`).
- All models use C# records with `init` properties. `ClaudeOptions` uses `record` with `with` expressions for immutability.
- The project targets `net10.0` with `LangVersion=preview`, nullable enabled, implicit usings enabled.
- Root namespace is `HumanLayerAutomation`. Sub-namespaces: `.Providers`, `.Models`, `.Notifications`, `.Health`.
- The `generated/` directory is excluded from compilation (used for builder output).
