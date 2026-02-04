# Getting Started

Step-by-step guide to setting up and running AI automation with human oversight.

## Prerequisites

### Required Software

| Software | Version | Purpose |
|----------|---------|---------|
| **.NET SDK** | 10.0+ | Runtime and build tools |
| **HumanLayer Daemon (hld)** | Latest | Session and approval management |
| **Claude Code** | Latest | AI agent execution |

### Optional

| Software | Purpose |
|----------|---------|
| **OpenRouter API Key** | Access to alternative AI models |
| **Slack/Email** | Additional approval channels |

---

## Step 1: Install .NET 10

### Windows
```powershell
# Using winget
winget install Microsoft.DotNet.SDK.10

# Or download from https://dotnet.microsoft.com/download
```

### macOS
```bash
# Using Homebrew
brew install dotnet@10

# Or download from https://dotnet.microsoft.com/download
```

### Linux
```bash
# Ubuntu/Debian
wget https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb
sudo apt update
sudo apt install dotnet-sdk-10.0

# Or use the install script
curl -sSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin --channel 10.0
```

Verify installation:
```bash
dotnet --version
# Should show 10.x.x
```

---

## Step 2: Install HumanLayer Daemon

### Option A: Via npm (Recommended)
```bash
npm install -g @anthropic/hld

# Verify installation
hld --version
```

### Option B: Build from source
```bash
git clone https://github.com/humanlayer/humanlayer.git
cd humanlayer/hld
go build -o hld ./cmd/hld
sudo mv hld /usr/local/bin/
```

---

## Step 3: Install Claude Code

```bash
npm install -g @anthropic/claude-code

# Verify installation
claude --version

# Authenticate (follow prompts)
claude auth
```

---

## Step 4: Start the Daemon

```bash
# Start in foreground (for development)
hld daemon start

# Or start as background service
hld daemon start --background

# Verify it's running
curl http://localhost:7777/api/v1/health
# Should return: {"status":"ok","version":"..."}
```

---

## Step 5: Clone and Build the .NET Client

```bash
# Clone the repository
git clone https://github.com/yourusername/humanlayer-dotnet.git
cd humanlayer-dotnet

# Restore dependencies
dotnet restore

# Build
dotnet build
```

---

## Step 6: Run Your First Automation

### Test Connectivity
```bash
cd HumanLayerAutomation
dotnet run -- demo
```

Expected output:
```
╔══════════════════════════════════════════════════════════════╗
║       HumanLayer .NET Automation Examples                    ║
╚══════════════════════════════════════════════════════════════╝

=== Demo Mode ===
Demonstrating HumanLayer API capabilities...

1. Checking daemon health...
   Status: ok, Version: 0.1.0

2. Listing existing sessions...
   Found 0 active sessions

3. Checking pending approvals...
   Found 0 pending approvals

=== Demo Complete ===
```

### Run a Simple Task
```bash
# Set your working directory (from project root)
cd HumanLayerAutomation
export WORKING_DIR="/path/to/your/project"

# Run with auto-approve for safe demo
dotnet run -- parallel
```

---

## Configuration

### Environment Variables

Create a `.env` file or set these variables:

```bash
# Required
export HUMANLAYER_URL="http://localhost:7777/api/v1"

# Optional
export WORKING_DIR="/path/to/your/project"
export OPENROUTER_API_KEY="sk-or-..."  # For alternative AI models
```

### Configuration File (Optional)

Create `appsettings.json`:
```json
{
  "HumanLayer": {
    "BaseUrl": "http://localhost:7777/api/v1",
    "DefaultModel": "sonnet",
    "DefaultMaxTurns": 50
  },
  "OpenRouter": {
    "BaseUrl": "https://openrouter.ai/api/v1",
    "DefaultModel": "anthropic/claude-3-haiku"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information"
    }
  }
}
```

---

## Running Different Modes

### Demo Mode
Quick API demonstration:
```bash
dotnet run -- demo
```

### Batch Processor
Process approvals in batches with human review:
```bash
dotnet run -- batch
```

### Scheduler
Run tasks on a schedule:
```bash
dotnet run -- scheduler
```

### Event Monitor
Watch events in real-time:
```bash
dotnet run -- monitor
```

### Parallel Runner
Execute multiple tasks concurrently:
```bash
dotnet run -- parallel
```

---

## Creating Your First Custom Task

### Simple Analysis Task

```csharp
using HumanLayerAutomation;

var client = new HumanLayerClient();

// Create a safe, read-only task
var result = await client.RunTaskAsync(new AutomationTask
{
    Name = "Code Analysis",
    Query = "Analyze the code structure and identify the main components",
    WorkingDir = Environment.GetEnvironmentVariable("WORKING_DIR"),
    Model = "haiku",  // Cheapest model for analysis
    MaxTurns = 10,
    AutoApprove = true,
    AutoApproveTimeout = TimeSpan.FromMinutes(2),
    AllowedTools = new[] { "Read", "Glob", "Grep" }
});

Console.WriteLine($"Status: {result.Status}");
Console.WriteLine($"Cost: ${result.CostUsd:F4}");
Console.WriteLine($"Summary: {result.Summary}");
```

### Task Requiring Human Approval

```csharp
// Create a task that requires approval for code changes
var sessionData = await client.CreateSessionAsync(new CreateSessionRequest
{
    Query = "Refactor the authentication module to use JWT tokens",
    WorkingDir = "/path/to/project",
    Model = "sonnet",
    DangerouslySkipPermissions = false  // Require approval for all tools
});

Console.WriteLine($"Session started: {sessionData.SessionId}");
Console.WriteLine("Approvals will appear in the terminal or Web UI");

// Wait for completion with human approval
var session = await client.WaitForSessionAsync(sessionData.SessionId);
Console.WriteLine($"Completed: {session.Status}");
```

---

## Project Structure

```
humanlayer-dotnet/
├── HumanLayerAutomation/          # Main project directory
│   ├── HumanLayerAutomation.csproj # Project file
│   ├── Program.cs                 # Application entry point
│   ├── HumanLayerClient.cs        # API client implementation
│   ├── Models.cs                  # Data models
│   ├── bin/                       # Build output (ignored by git)
│   └── obj/                       # Build artifacts (ignored by git)
├── docs/                          # Documentation
│   ├── getting-started.md         # This file
│   ├── architecture.md            # System architecture
│   ├── concepts.md                # Core concepts
│   ├── patterns.md                # Common patterns
│   ├── security.md                # Security guidelines
│   ├── api-reference.md           # API documentation
│   └── roadmap.md                 # Project roadmap
├── dotnet-automation.sln          # Solution file (entry point for builds)
├── README.md                      # Project overview
└── .gitignore                     # Git ignore rules (.NET specific)
```

### Running Commands

All `dotnet run` commands must be executed from the `HumanLayerAutomation/` directory:

```bash
# ✓ Correct - Running from project directory
cd HumanLayerAutomation
dotnet run -- demo

# ✓ Also correct - Running from solution root with project specification
dotnet run --project HumanLayerAutomation -- demo

# ✗ Incorrect - Running from solution root without specification
dotnet run -- demo  # Error: multiple projects found
```

---

### Daemon not running
```
Error: Cannot connect to daemon
```
**Solution**: Start the daemon with `hld daemon start`

### Claude Code not found
```
Error: Claude binary not available
```
**Solution**: Install Claude Code with `npm install -g @anthropic/claude-code` and authenticate with `claude auth`

### Session stuck in "starting"
```
Status: starting (for extended time)
```
**Solution**: Check daemon logs with `hld logs`, ensure Claude Code is properly authenticated

### Approvals not appearing
```
No approvals found but session is waiting
```
**Solution**: Ensure the MCP configuration is correct in the session request

---

## Next Steps

1. **Read the [Concepts](concepts.md)** to understand the terminology
2. **Explore [Patterns](patterns.md)** for automation best practices
3. **Review [Security](security.md)** before production deployment
4. **Check the [API Reference](api-reference.md)** for all available endpoints

---

## Getting Help

- **Issues**: https://github.com/humanlayer/humanlayer/issues
- **Discussions**: https://github.com/humanlayer/humanlayer/discussions
- **Documentation**: https://humanlayer.dev/docs
