#!/bin/bash
# build-todo-app.sh
# Linux/macOS script to build a Todo app using Claude Code automation
# Features: Self-healing builds with automatic error correction
#
# Usage: ./build-todo-app.sh [output-dir] [max-retries]

set -e

OUTPUT_DIR="${1:-./generated/TodoCli}"
MAX_RETRIES="${2:-3}"
BUILD_SUCCESS=false

# Helper function to extract clean code from Claude's response
extract_code() {
    local response="$1"
    local language="$2"
    local code="$response"

    # Remove markdown code fences
    if echo "$code" | grep -q '```'; then
        code=$(echo "$code" | sed -n '/```/,/```/p' | sed '1d;$d')
    fi

    # For XML, ensure we start with <?xml or <Project
    if [ "$language" = "xml" ]; then
        code=$(echo "$code" | sed -n '/<\?xml\|<Project/,$p')
    fi

    # For C#, start from first 'using' statement
    if [ "$language" = "csharp" ]; then
        code=$(echo "$code" | sed -n '/^using\|^\/\//,$p')
    fi

    echo "$code"
}

# Helper function to run Claude and get clean output
invoke_claude() {
    local prompt="$1"
    local model="${2:-sonnet}"
    local language="${3:-}"

    local response
    response=$(claude --print "$prompt" --model "$model" --dangerously-skip-permissions 2>&1)
    extract_code "$response" "$language"
}

echo ""
echo "╔══════════════════════════════════════════════════════════════╗"
echo "║   Build Todo App with Claude Code Automation (Linux/macOS)   ║"
echo "║   Self-Healing Mode: Up to $MAX_RETRIES fix attempts                        ║"
echo "╚══════════════════════════════════════════════════════════════╝"
echo ""

# Check prerequisites
echo "Checking prerequisites..."

# Check Claude CLI
if command -v claude &> /dev/null; then
    CLAUDE_VERSION=$(claude --version 2>&1)
    echo "  ✓ Claude CLI: $CLAUDE_VERSION"
else
    echo "  ✗ Claude CLI not found."
    echo "    Install with: curl -fsSL https://claude.ai/install.sh | bash"
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
OUTPUT_DIR=$(cd "$OUTPUT_DIR" && pwd)

# Get script directory and project root
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"

cd "$PROJECT_ROOT"

# Step 1: Design
echo ""
echo "STEP 1: Designing application architecture..."
echo "─────────────────────────────────────────────"

DESIGN_PROMPT='Design a simple C# command-line Todo application with add, list, complete, delete commands. Store todos in JSON. Include priority levels. Keep it minimal. Provide a brief 3-5 bullet point design.'

echo "Running design prompt..."
design=$(invoke_claude "$DESIGN_PROMPT" "haiku")
echo "$design"
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
- Top-level statements (no namespace, no class wrapper for Main)
- Manual argument parsing (no external libs)
- Store in todos.json in current directory
- Clean, user-friendly output
- Reference TodoItem and TodoStore from TodoItem.cs

IMPORTANT: Output ONLY the raw C# code. No markdown fences, no explanations. Start directly with using statements.'

echo "Generating code with Claude (sonnet)..."
invoke_claude "$PROGRAM_PROMPT" "sonnet" "csharp" > "$OUTPUT_DIR/Program.cs"
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
3. TodoStore class with static methods:
   - List<TodoItem> Load(string path) - reads JSON, returns empty list if file missing
   - void Save(string path, List<TodoItem> items) - writes JSON with indentation
   - int GetNextId(List<TodoItem> items) - returns max Id + 1, or 1 if empty

Use System.Text.Json with WriteIndented = true.
IMPORTANT: Output ONLY the raw C# code. No markdown fences, no explanations. Start directly with using statements.'

echo "Generating models with Claude (sonnet)..."
invoke_claude "$MODELS_PROMPT" "sonnet" "csharp" > "$OUTPUT_DIR/TodoItem.cs"
LINES=$(wc -l < "$OUTPUT_DIR/TodoItem.cs")
echo "  ✓ TodoItem.cs generated ($LINES lines)"

# Step 4: Generate .csproj
echo ""
echo "STEP 4: Generating project file..."
echo "─────────────────────────────────────────────"

CSPROJ_PROMPT='Generate a .csproj file for a .NET 8 console application.

Requirements:
- OutputType: Exe
- TargetFramework: net8.0
- Nullable: enable
- ImplicitUsings: enable
- No NuGet packages needed

IMPORTANT: Output ONLY the raw XML. No markdown fences, no explanations. Start directly with <Project or <?xml.'

echo "Generating project file with Claude (haiku)..."
invoke_claude "$CSPROJ_PROMPT" "haiku" "xml" > "$OUTPUT_DIR/TodoCli.csproj"
echo "  ✓ TodoCli.csproj generated"

# Step 5: Build with self-healing
echo ""
echo "STEP 5: Building the generated application..."
echo "─────────────────────────────────────────────"

cd "$OUTPUT_DIR"

attempt=0
while [ $attempt -lt $MAX_RETRIES ] && [ "$BUILD_SUCCESS" = false ]; do
    attempt=$((attempt + 1))
    echo ""
    echo "  Build attempt $attempt of $MAX_RETRIES..."

    set +e
    BUILD_OUTPUT=$(dotnet build 2>&1)
    BUILD_EXIT_CODE=$?
    set -e

    if [ $BUILD_EXIT_CODE -eq 0 ]; then
        echo "  ✓ Build successful!"
        BUILD_SUCCESS=true
    else
        echo "  ✗ Build failed:"
        echo "$BUILD_OUTPUT"

        if [ $attempt -lt $MAX_RETRIES ]; then
            echo ""
            echo "  Sending errors to Claude for auto-fix..."

            # Determine which file has the error
            ERROR_FILE=""
            if echo "$BUILD_OUTPUT" | grep -q "Program\.cs"; then
                ERROR_FILE="Program.cs"
            elif echo "$BUILD_OUTPUT" | grep -q "TodoItem\.cs"; then
                ERROR_FILE="TodoItem.cs"
            elif echo "$BUILD_OUTPUT" | grep -q "\.csproj"; then
                ERROR_FILE="TodoCli.csproj"
            fi

            if [ -n "$ERROR_FILE" ]; then
                CURRENT_CODE=$(cat "$OUTPUT_DIR/$ERROR_FILE")

                FIX_PROMPT="The following code has compilation errors. Fix ALL the errors and return the COMPLETE corrected file.

ERRORS:
$BUILD_OUTPUT

CURRENT CODE:
$CURRENT_CODE

IMPORTANT: Output ONLY the corrected code. No markdown fences, no explanations. The code must compile without errors."

                echo "  Fixing $ERROR_FILE..."
                LANG="csharp"
                if echo "$ERROR_FILE" | grep -q "\.csproj"; then
                    LANG="xml"
                fi
                invoke_claude "$FIX_PROMPT" "sonnet" "$LANG" > "$OUTPUT_DIR/$ERROR_FILE"
                echo "  ✓ Applied fix to $ERROR_FILE"
            else
                echo "  ⚠ Could not determine which file to fix"
            fi
        fi
    fi
done

cd "$PROJECT_ROOT"

# Summary
echo ""
echo "═══════════════════════════════════════════════"
if [ "$BUILD_SUCCESS" = true ]; then
    echo "BUILD SUCCESSFUL!"
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
else
    echo "BUILD FAILED after $MAX_RETRIES attempts"
    echo "═══════════════════════════════════════════════"
    echo ""
    echo "Generated files:"
    ls -la "$OUTPUT_DIR"
    echo ""
    echo "Try manually reviewing and fixing the errors, or run again."
    exit 1
fi
echo ""
