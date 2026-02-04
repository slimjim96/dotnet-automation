# System Architecture

This document describes the architecture of the HumanLayer ecosystem and how the .NET automation client integrates with it.

## High-Level Overview

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                         Your Application Layer                               │
│  ┌──────────────────────────────────────────────────────────────────────┐   │
│  │                    .NET Automation Client                             │   │
│  │  ┌────────────┐  ┌────────────┐  ┌────────────┐  ┌────────────┐      │   │
│  │  │ Scheduler  │  │   Batch    │  │  Parallel  │  │   Event    │      │   │
│  │  │  Runner    │  │ Processor  │  │   Runner   │  │  Monitor   │      │   │
│  │  └─────┬──────┘  └─────┬──────┘  └─────┬──────┘  └─────┬──────┘      │   │
│  │        └───────────────┴───────────────┴───────────────┘              │   │
│  │                                │                                       │   │
│  │                    ┌───────────▼───────────┐                          │   │
│  │                    │   HumanLayerClient    │                          │   │
│  │                    │   (REST API Client)   │                          │   │
│  │                    └───────────┬───────────┘                          │   │
│  └────────────────────────────────┼──────────────────────────────────────┘   │
└───────────────────────────────────┼──────────────────────────────────────────┘
                                    │ HTTP/REST (Port 7777)
                                    ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                        HumanLayer Daemon (hld)                               │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐  ┌─────────────────┐    │
│  │   Session   │  │  Approval   │  │   Event     │  │   REST API      │    │
│  │   Manager   │  │   Manager   │  │    Bus      │  │   + JSON-RPC    │    │
│  └──────┬──────┘  └──────┬──────┘  └──────┬──────┘  └────────┬────────┘    │
│         │                │                │                   │             │
│         └────────────────┴────────────────┴───────────────────┘             │
│                                    │                                         │
│                         ┌──────────▼──────────┐                             │
│                         │  SQLite Database    │                             │
│                         │ (~/.humanlayer/     │                             │
│                         │    daemon.db)       │                             │
│                         └─────────────────────┘                             │
└─────────────────────────────────────────────────────────────────────────────┘
          │                         │                         │
          ▼                         ▼                         ▼
┌─────────────────┐      ┌─────────────────┐      ┌─────────────────────────┐
│   Claude Code   │      │  Human Review   │      │   Alternative Models    │
│   CLI Process   │      │  Channels:      │      │   via Proxy:            │
│                 │      │  • Web UI       │      │   • OpenRouter          │
│   (AI Agent)    │      │  • CLI          │      │   • GPT-4, Llama, etc.  │
│                 │      │  • Slack/Email  │      │                         │
└─────────────────┘      └─────────────────┘      └─────────────────────────┘
```

## Component Descriptions

### .NET Automation Client (This Project)

The automation client provides a high-level interface for:

- **Session Management**: Launch, monitor, and control AI agent sessions
- **Approval Processing**: Review and decide on pending tool approvals
- **Event Streaming**: Real-time notifications via Server-Sent Events
- **Task Orchestration**: Schedule and parallelize AI workloads

### HumanLayer Daemon (hld)

The daemon is the central orchestrator that coordinates all operations:

| Component | Responsibility |
|-----------|----------------|
| **Session Manager** | Launches Claude Code processes, tracks lifecycle, manages state transitions |
| **Approval Manager** | Creates approval requests, correlates with tool calls, records decisions |
| **Event Bus** | Publishes events across components, enables real-time subscriptions |
| **REST API** | HTTP interface at port 7777 for external integrations |
| **SQLite Store** | Persists sessions, approvals, conversations, and snapshots |

### Claude Code

The AI agent that executes tasks:

- Runs as a separate process managed by the daemon
- Communicates via MCP (Model Context Protocol) for tool approvals
- Supports multiple models: Opus (most capable), Sonnet (balanced), Haiku (fast/cheap)

### Human Review Channels

Multiple channels for human oversight:

- **Web UI (CodeLayer)**: Full-featured desktop/web application
- **CLI**: Terminal-based approval prompts
- **Slack**: Message-based approvals for team workflows
- **Email**: Asynchronous approval for non-urgent items

### Alternative Model Proxy

Route requests through external providers:

- **OpenRouter**: Gateway to 100+ models (GPT-4, Llama, Mistral, etc.)
- **Baseten**: MLOps platform for custom models
- **Custom Endpoints**: Any OpenAI-compatible API

## Data Flow

### Session Lifecycle

```
1. Client calls CreateSession
       ↓
2. Daemon creates session record (status: starting)
       ↓
3. Daemon launches Claude Code process
       ↓
4. Claude Code starts working (status: running)
       ↓
5. Claude needs to use a tool
       ↓
6. MCP request_approval tool invoked
       ↓
7. Daemon creates approval (status: waiting_input)
       ↓
8. Event published: "new_approval"
       ↓
9. Human reviews and decides
       ↓
10. Daemon records decision
       ↓
11. Claude Code resumes (status: running)
       ↓
12. Work completes (status: completed/failed)
       ↓
13. Event published: "session_status_changed"
```

### Approval Flow

```
┌─────────────────┐     ┌─────────────────┐     ┌─────────────────┐
│  Claude Code    │────▶│  MCP Server     │────▶│  Daemon         │
│  (tool_call)    │     │  (stdio/HTTP)   │     │  (create)       │
└─────────────────┘     └─────────────────┘     └────────┬────────┘
                                                         │
                    ┌────────────────────────────────────┘
                    ▼
         ┌─────────────────────────────────────────────────┐
         │              Approval Created                    │
         │  • ID: appr_xxx                                  │
         │  • Status: pending                               │
         │  • Tool: Bash                                    │
         │  • Input: {"command": "rm -rf /tmp/test"}        │
         └────────────────────────┬────────────────────────┘
                                  │
                    ┌─────────────┴─────────────┐
                    ▼                           ▼
         ┌─────────────────┐          ┌─────────────────┐
         │  .NET Client    │          │  Web UI         │
         │  (batch mode)   │          │  (real-time)    │
         └────────┬────────┘          └────────┬────────┘
                  │                            │
                  └─────────────┬──────────────┘
                                ▼
         ┌─────────────────────────────────────────────────┐
         │           Human Decision                         │
         │  POST /approvals/{id}/decide                     │
         │  { "decision": "approve", "comment": "OK" }      │
         └────────────────────────┬────────────────────────┘
                                  │
                                  ▼
         ┌─────────────────────────────────────────────────┐
         │           Claude Code Resumes                    │
         │  Tool result returned, work continues            │
         └─────────────────────────────────────────────────┘
```

## Network Architecture

### Default Configuration

```
┌───────────────────────────────────────────────────────────┐
│                     Local Machine                          │
│                                                            │
│   .NET Client ◄──── HTTP :7777 ────► hld daemon           │
│                                          │                 │
│                     Unix Socket ─────────┘                 │
│                  ~/.humanlayer/daemon.sock                 │
│                                                            │
└───────────────────────────────────────────────────────────┘
```

### Remote/Distributed Configuration

```
┌─────────────────────┐        ┌─────────────────────┐
│   Client Machine    │        │   Server Machine    │
│                     │        │                     │
│   .NET Client ──────┼─ HTTP ─┼──► hld daemon      │
│                     │ :7777  │        │            │
│                     │        │        ▼            │
│                     │        │   Claude Code      │
│                     │        │   (local process)   │
└─────────────────────┘        └─────────────────────┘
```

## Storage Architecture

### SQLite Database Schema

```
~/.humanlayer/daemon.db
│
├── sessions
│   ├── id (PRIMARY KEY)
│   ├── run_id
│   ├── claude_session_id
│   ├── status
│   ├── query, title, summary
│   ├── model, working_dir
│   ├── auto_accept_edits
│   ├── dangerously_skip_permissions
│   ├── proxy_enabled, proxy_base_url, proxy_model_override
│   ├── cost_usd, input_tokens, output_tokens
│   └── created_at, last_activity_at, completed_at
│
├── approvals
│   ├── id (PRIMARY KEY)
│   ├── run_id, session_id
│   ├── tool_name, tool_input (JSON)
│   ├── status (pending/approved/denied)
│   ├── tool_use_id (correlation)
│   ├── comment
│   └── created_at, responded_at
│
├── conversation_events
│   ├── id (PRIMARY KEY)
│   ├── session_id, sequence
│   ├── event_type (message/tool_call/tool_result)
│   ├── role, content
│   ├── tool_id, tool_name, tool_input_json
│   └── created_at
│
├── file_snapshots
│   ├── tool_id, file_path, content
│   └── created_at
│
└── user_settings
    ├── advanced_providers
    ├── opt_in_telemetry
    └── updated_at
```

## Scalability Considerations

### Single-Machine Deployment

- SQLite handles thousands of sessions efficiently
- Event bus is in-memory, no external dependencies
- Suitable for individual developers and small teams

### Multi-Machine Deployment

For larger deployments:

1. **Run daemon on dedicated server**: Expose HTTP API to network
2. **Load balance clients**: Multiple .NET clients can connect
3. **Consider external database**: Replace SQLite with PostgreSQL for production
4. **Event streaming**: Use message queue (Redis, RabbitMQ) for distributed events

### Token/Cost Optimization

```
┌─────────────────────────────────────────────────────────────┐
│                    Token Budget Strategy                     │
│                                                              │
│   ┌─────────────┐    ┌─────────────┐    ┌─────────────┐     │
│   │   Haiku     │    │   Sonnet    │    │    Opus     │     │
│   │  (Cheap)    │    │  (Balanced) │    │  (Powerful) │     │
│   │             │    │             │    │             │     │
│   │ • Analysis  │    │ • Coding    │    │ • Complex   │     │
│   │ • Simple    │    │ • Standard  │    │   reasoning │     │
│   │   tasks     │    │   work      │    │ • Critical  │     │
│   │             │    │             │    │   tasks     │     │
│   └─────────────┘    └─────────────┘    └─────────────┘     │
│                                                              │
│   Cost ratio:  1x          3x               15x              │
└─────────────────────────────────────────────────────────────┘
```

## Next Steps

- [Core Concepts](concepts.md) - Understand terminology and principles
- [API Reference](api-reference.md) - Detailed endpoint documentation
- [Automation Patterns](patterns.md) - Best practices for automation
