#!/bin/bash
# build-todo-app.sh
# Linux/macOS script to build a Todo app using Claude Code automation
#
# Usage: ./build-todo-app.sh [output-dir]

set -e

OUTPUT_DIR="${1:-./generated/TodoCli}"

echo ""
echo "╔══════════════════════════════════════════════════════════════╗"
echo "║   Build Todo App with Claude Code Automation (Linux/macOS)   ║"
echo "╚══════════════════════════════════════════════════════════════╝"
echo ""

# Check prerequisites
echo "Checking prerequisites..."

# Check Claude CLI
if command -v claude &> /dev/null; then
    CLAUDE_VERSION=$(claude --version 2>&1)
    echo "  ✓ Claude CLI: $CLAUDE_VERSION"
else
    echo "  ✗ Claude CLI not found. Install with: npm install -g @anthropic/claude-code"
    exit 1
fi

# Check .NET
if command -v dotnet &> /dev/null; then
    DOTNET_VERSION=$(dotnet --version 2>&1)
    echo "  ✓ .NET SDK: $DOTNET_VERSION"
else
    echo "  ✗ .NET SDK not found. Install from https://dotnet.microsoft.com"
    exit 1
fi

# Create output directory
echo ""
echo "Creating output directory: $OUTPUT_DIR"
mkdir -p "$OUTPUT_DIR"

# Get script directory and project root
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"

cd "$PROJECT_ROOT"

# Step 1: Design
echo ""
echo "STEP 1: Designing application architecture..."
echo "─────────────────────────────────────────────"

DESIGN_PROMPT='Design a simple C# command-line Todo application:
- Commands: add, list, complete, delete
- Store todos in a JSON file
- Include priority levels (low, medium, high)
- Keep it minimal, no external packages

Provide a brief design (3-5 bullet points) and list files to create.'

dotnet run --project HumanLayerAutomation -- run "$DESIGN_PROMPT" || true
echo ""
echo "  ✓ Design complete"

# Step 2: Generate Program.cs
echo ""
echo "STEP 2: Generating Program.cs..."
echo "─────────────────────────────────────────────"

PROGRAM_PROMPT='Generate a complete C# Program.cs for a Todo CLI app.

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

Output ONLY valid C# code, no markdown or explanations.'

claude --print "$PROGRAM_PROMPT" --model sonnet > "$OUTPUT_DIR/Program.cs"
LINES=$(wc -l < "$OUTPUT_DIR/Program.cs")
echo "  ✓ Program.cs generated ($LINES lines)"

# Step 3: Generate TodoItem.cs
echo ""
echo "STEP 3: Generating TodoItem.cs (models)..."
echo "─────────────────────────────────────────────"

MODELS_PROMPT='Generate a C# file TodoItem.cs containing:

1. Priority enum: Low = 0, Medium = 1, High = 2
2. TodoItem record with properties:
   - int Id
   - string Title
   - bool IsCompleted
   - Priority Priority
   - DateTime CreatedAt
3. TodoStore class with methods:
   - List<TodoItem> Load(string path)
   - void Save(string path, List<TodoItem> items)
   - int GetNextId(List<TodoItem> items)

Use System.Text.Json with options for pretty printing.
Output ONLY valid C# code, no markdown or explanations.'

claude --print "$MODELS_PROMPT" --model sonnet > "$OUTPUT_DIR/TodoItem.cs"
LINES=$(wc -l < "$OUTPUT_DIR/TodoItem.cs")
echo "  ✓ TodoItem.cs generated ($LINES lines)"

# Step 4: Generate .csproj
echo ""
echo "STEP 4: Generating project file..."
echo "─────────────────────────────────────────────"

CSPROJ_PROMPT='Generate a .csproj file for a .NET 8 console application:
- Project name: TodoCli
- OutputType: Exe
- TargetFramework: net8.0
- Nullable: enable
- ImplicitUsings: enable
- No NuGet packages needed

Output ONLY valid XML, no markdown or explanations.'

claude --print "$CSPROJ_PROMPT" --model haiku > "$OUTPUT_DIR/TodoCli.csproj"
echo "  ✓ TodoCli.csproj generated"

# Step 5: Build the project
echo ""
echo "STEP 5: Building the generated application..."
echo "─────────────────────────────────────────────"

cd "$OUTPUT_DIR"
if dotnet build; then
    echo "  ✓ Build successful!"
else
    echo "  ⚠ Build had warnings/errors (see above)"
fi
cd "$PROJECT_ROOT"

# Summary
echo ""
echo "═══════════════════════════════════════════════"
echo "BUILD COMPLETE!"
echo "═══════════════════════════════════════════════"
echo ""
echo "Generated files:"
ls -la "$OUTPUT_DIR"
echo ""
echo "To run the app:"
echo "  cd $OUTPUT_DIR"
echo '  dotnet run -- add "My first task" --priority high'
echo '  dotnet run -- list'
echo '  dotnet run -- complete 1'
echo '  dotnet run -- delete 1'
echo ""
