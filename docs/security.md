# Security Considerations

Security best practices for running AI automation with human oversight.

## Threat Model

When running AI agents with tool access, consider these threats:

| Threat | Description | Mitigation |
|--------|-------------|------------|
| **Unauthorized Commands** | AI executes harmful commands | Approval system, tool whitelisting |
| **Data Exfiltration** | AI accesses and leaks sensitive data | Directory restrictions, network controls |
| **Privilege Escalation** | AI gains more access than intended | Least privilege, sandboxing |
| **Prompt Injection** | Malicious input manipulates AI | Input validation, context isolation |
| **Credential Exposure** | API keys leaked in logs/output | Secret management, log sanitization |

---

## Daemon Security

### Filesystem Isolation

The daemon uses filesystem-based security:

```
~/.humanlayer/
├── daemon.sock    # Unix socket (permissions: 0600)
├── daemon.db      # SQLite database
└── logs/          # Log files
```

**Unix Socket Security**:
- Socket permissions are set to `0600` (owner read/write only)
- Other users cannot connect
- No network exposure by default

### Network Exposure

By default, the HTTP API binds to localhost only:

```bash
# Default: only accessible locally
http://127.0.0.1:7777

# To expose to network (not recommended without additional security):
# hld daemon start --http-host 0.0.0.0
```

**If exposing to network**:
1. Use a reverse proxy with authentication (nginx, Caddy, etc.)
2. Enable TLS/HTTPS
3. Implement API key authentication
4. Use firewall rules to restrict access

Example nginx configuration:
```nginx
server {
    listen 443 ssl;
    server_name humanlayer.internal;

    ssl_certificate /path/to/cert.pem;
    ssl_certificate_key /path/to/key.pem;

    location /api/ {
        auth_basic "HumanLayer";
        auth_basic_user_file /etc/nginx/.htpasswd;

        proxy_pass http://127.0.0.1:7777/api/;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
    }
}
```

---

## Approval Security

### The Determinism Guarantee

HumanLayer's key security property is **deterministic approval**:

```
Traditional approach (vulnerable):
┌─────────┐     ┌────────────┐     ┌─────────┐
│   AI    │────▶│   Filter   │────▶│  Action │
└─────────┘     └────────────┘     └─────────┘
                     ▲
                     │ (Can be bypassed by clever prompting)

HumanLayer approach (secure):
┌─────────┐     ┌────────────────────────┐     ┌─────────┐
│   AI    │────▶│ Tool with built-in     │────▶│  Action │
└─────────┘     │ approval requirement   │     └─────────┘
                └────────────────────────┘
                             │
                             ▼
                    (Cannot be bypassed)
```

The approval is embedded in the tool definition itself, not as a post-processing filter. The AI cannot "convince" the system to skip approval.

### Approval Best Practices

1. **Default to requiring approval**:
   ```csharp
   // Secure default
   DangerouslySkipPermissions = false
   ```

2. **Use timeouts for auto-approve**:
   ```csharp
   // Auto-approve expires after 5 minutes
   DangerouslySkipPermissions = true,
   DangerouslySkipPermissionsTimeoutMs = 300000
   ```

3. **Whitelist safe tools only**:
   ```csharp
   AllowedTools = new[] { "Read", "Glob", "Grep" }
   ```

4. **Review denied approvals**:
   ```csharp
   // Log all denials for security review
   await client.DenyAsync(approvalId, "Suspicious command pattern");
   await SecurityLogger.LogDenialAsync(approval);
   ```

---

## Credential Management

### API Keys

Never hardcode API keys:

```csharp
// ❌ DON'T
var client = new HumanLayerClient();
var task = new AutomationTask
{
    ProxyApiKey = "sk-or-abc123..."  // Hardcoded!
};

// ✅ DO
var task = new AutomationTask
{
    ProxyApiKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY")
};
```

### Environment Variables

```bash
# Use .env files (not committed to git)
export OPENROUTER_API_KEY="sk-or-..."
export HUMANLAYER_URL="http://localhost:7777/api/v1"

# Or use a secrets manager
export OPENROUTER_API_KEY=$(vault kv get -field=key secrets/openrouter)
```

### Secrets in Tool Inputs

The AI may accidentally include secrets in tool inputs:

```csharp
// Review approval inputs for secrets before auto-approving
public bool ContainsSecrets(string input)
{
    var patterns = new[]
    {
        @"sk-[a-zA-Z0-9]{20,}",           // OpenAI/OpenRouter keys
        @"ghp_[a-zA-Z0-9]{36}",           // GitHub tokens
        @"AKIA[0-9A-Z]{16}",              // AWS access keys
        @"password\s*[=:]\s*['""]?\w+",   // Passwords
    };

    return patterns.Any(p => Regex.IsMatch(input, p, RegexOptions.IgnoreCase));
}

// In approval processing
if (ContainsSecrets(JsonSerializer.Serialize(approval.ToolInput)))
{
    await client.DenyAsync(approval.Id, "Potential secret detected in input");
    await AlertSecurityTeamAsync(approval);
}
```

---

## Tool Restrictions

### Whitelisting

Explicitly allow only needed tools:

```csharp
// Most restrictive: only read operations
AllowedTools = new[] { "Read" }

// Analysis tools
AllowedTools = new[] { "Read", "Glob", "Grep", "LS" }

// Development tools (requires human approval)
AllowedTools = new[] { "Read", "Write", "Edit", "Glob", "Grep" }
```

### Blacklisting

Block dangerous tools:

```csharp
// Block shell access
DisallowedTools = new[] { "Bash" }

// Block network access
DisallowedTools = new[] { "WebFetch", "WebSearch", "Bash" }
```

### Bash Command Filtering

When allowing Bash, restrict what commands can run:

```csharp
// In approval processing
public bool IsSafeBashCommand(Dictionary<string, object> toolInput)
{
    var command = toolInput["command"]?.ToString() ?? "";

    // Block dangerous patterns
    var dangerousPatterns = new[]
    {
        @"rm\s+-rf",              // Recursive delete
        @">\s*/dev/",             // Write to devices
        @"curl.*\|.*sh",          // Pipe to shell
        @"wget.*\|.*sh",          // Pipe to shell
        @"chmod\s+777",           // World-writable
        @"sudo",                  // Privilege escalation
        @"ssh",                   // Remote access
        @"nc\s+-[el]",            // Netcat listeners
    };

    return !dangerousPatterns.Any(p =>
        Regex.IsMatch(command, p, RegexOptions.IgnoreCase));
}
```

---

## Directory Restrictions

### Working Directory

Restrict AI to specific directories:

```csharp
// Only allow access to project directory
WorkingDir = "/home/user/projects/myapp"

// Add read-only access to documentation
AdditionalDirectories = new[] { "/docs" }  // (if supported)
```

### Sensitive Directories

Never allow access to:
- `~/.ssh/` - SSH keys
- `~/.aws/` - AWS credentials
- `~/.config/` - Application configs
- `/etc/` - System configuration
- Environment files with secrets

---

## Logging and Auditing

### What to Log

```csharp
public class SecurityLogger
{
    public async Task LogSessionAsync(Session session)
    {
        await Log(new
        {
            Event = "session_created",
            SessionId = session.Id,
            Query = session.Query,
            Model = session.Model,
            WorkingDir = session.WorkingDir,
            AutoApprove = session.DangerouslySkipPermissions,
            Timestamp = DateTime.UtcNow
        });
    }

    public async Task LogApprovalAsync(Approval approval, string decision)
    {
        await Log(new
        {
            Event = "approval_decision",
            ApprovalId = approval.Id,
            SessionId = approval.SessionId,
            ToolName = approval.ToolName,
            Decision = decision,
            // Don't log full tool input - may contain secrets
            InputHash = ComputeHash(approval.ToolInput),
            Timestamp = DateTime.UtcNow
        });
    }
}
```

### Log Sanitization

Remove sensitive data from logs:

```csharp
public string SanitizeForLogging(string input)
{
    // Redact API keys
    input = Regex.Replace(input, @"sk-[a-zA-Z0-9]+", "sk-[REDACTED]");

    // Redact passwords
    input = Regex.Replace(input, @"password['""]?\s*[=:]\s*['""]?[^'""]+",
        "password=[REDACTED]");

    return input;
}
```

---

## Network Security

### Outbound Restrictions

If running in a controlled environment:

```bash
# Firewall: Only allow outbound to specific hosts
iptables -A OUTPUT -d api.anthropic.com -p tcp --dport 443 -j ACCEPT
iptables -A OUTPUT -d openrouter.ai -p tcp --dport 443 -j ACCEPT
iptables -A OUTPUT -j DROP
```

### Proxy Configuration

Route all AI traffic through a monitoring proxy:

```csharp
ProxyEnabled = true,
ProxyBaseUrl = "https://your-proxy.internal/v1"  // Your monitoring proxy
```

---

## Defense in Depth

Layer multiple security controls:

```
┌─────────────────────────────────────────────────────────┐
│ Layer 1: Network                                         │
│ • Firewall rules                                         │
│ • VPN/private network                                    │
├─────────────────────────────────────────────────────────┤
│ Layer 2: Authentication                                  │
│ • Reverse proxy with auth                               │
│ • API key management                                     │
├─────────────────────────────────────────────────────────┤
│ Layer 3: Authorization                                   │
│ • Tool whitelisting                                      │
│ • Directory restrictions                                 │
├─────────────────────────────────────────────────────────┤
│ Layer 4: Human Approval                                  │
│ • Deterministic approval gates                          │
│ • Tiered approval policies                              │
├─────────────────────────────────────────────────────────┤
│ Layer 5: Monitoring                                      │
│ • Audit logging                                          │
│ • Anomaly detection                                      │
│ • Alert on suspicious patterns                          │
└─────────────────────────────────────────────────────────┘
```

---

## Incident Response

### If Suspicious Activity Detected

1. **Immediately interrupt the session**:
   ```csharp
   await client.InterruptSessionAsync(sessionId);
   ```

2. **Review the conversation**:
   ```csharp
   var messages = await client.GetConversationAsync(sessionId);
   // Analyze what happened
   ```

3. **Archive for investigation**:
   ```csharp
   // Don't delete - preserve for forensics
   await client.UpdateSessionAsync(sessionId, new { Archived = true });
   ```

4. **Review all pending approvals**:
   ```csharp
   var pending = await client.GetPendingApprovalsAsync();
   foreach (var approval in pending.Where(a => a.SessionId == sessionId))
   {
       await client.DenyAsync(approval.Id, "Session under investigation");
   }
   ```

---

## Security Checklist

Before deploying to production:

- [ ] API keys stored in environment variables, not code
- [ ] Daemon only accessible via localhost or authenticated proxy
- [ ] Tool whitelist configured appropriately
- [ ] Auto-approve timeouts set
- [ ] Audit logging enabled
- [ ] Sensitive directories excluded
- [ ] Bash command filtering implemented
- [ ] Secret detection in approval inputs
- [ ] Incident response plan documented
- [ ] Regular security reviews scheduled

---

## Next Steps

- [Getting Started](getting-started.md) - Setup guide
- [Patterns](patterns.md) - Automation patterns
- [API Reference](api-reference.md) - Complete API documentation
