using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace HumanLayerAutomation;

/// <summary>
/// Client for interacting with the HumanLayer daemon REST API.
/// Provides session management, approval workflows, and event streaming.
/// </summary>
public class HumanLayerClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<HumanLayerClient>? _logger;
    private readonly JsonSerializerOptions _jsonOptions;
    private CancellationTokenSource? _sseCts;

    public string BaseUrl { get; }

    public HumanLayerClient(string baseUrl = "http://localhost:7777/api/v1", ILogger<HumanLayerClient>? logger = null)
    {
        BaseUrl = baseUrl.TrimEnd('/');
        _logger = logger;
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(BaseUrl),
            Timeout = TimeSpan.FromMinutes(5)
        };
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            WriteIndented = false
        };
    }

    // ========================================================================
    // Health & System
    // ========================================================================

    /// <summary>Check if the daemon is running and healthy.</summary>
    public async Task<HealthResponse> HealthAsync(CancellationToken ct = default)
    {
        var response = await _httpClient.GetAsync("/health", ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<HealthResponse>(_jsonOptions, ct)
            ?? throw new InvalidOperationException("Failed to parse health response");
    }

    /// <summary>Wait for the daemon to become healthy.</summary>
    public async Task WaitForHealthyAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        while (!cts.Token.IsCancellationRequested)
        {
            try
            {
                var health = await HealthAsync(cts.Token);
                if (health.Status == "ok")
                {
                    _logger?.LogInformation("Daemon is healthy");
                    return;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger?.LogDebug("Health check failed, retrying: {Message}", ex.Message);
            }

            await Task.Delay(500, cts.Token);
        }

        throw new TimeoutException("Daemon did not become healthy within timeout");
    }

    // ========================================================================
    // Session Management
    // ========================================================================

    /// <summary>Create and launch a new AI session.</summary>
    public async Task<CreateSessionData> CreateSessionAsync(CreateSessionRequest request, CancellationToken ct = default)
    {
        _logger?.LogInformation("Creating session with query: {Query}", request.Query[..Math.Min(50, request.Query.Length)]);

        var response = await _httpClient.PostAsJsonAsync("/sessions", request, _jsonOptions, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<CreateSessionResponse>(_jsonOptions, ct)
            ?? throw new InvalidOperationException("Failed to parse session response");

        _logger?.LogInformation("Created session: {SessionId}", result.Data.SessionId);
        return result.Data;
    }

    /// <summary>Get session details by ID.</summary>
    public async Task<Session> GetSessionAsync(string sessionId, CancellationToken ct = default)
    {
        var response = await _httpClient.GetAsync($"/sessions/{sessionId}", ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<SessionResponse>(_jsonOptions, ct)
            ?? throw new InvalidOperationException("Failed to parse session response");

        return result.Data;
    }

    /// <summary>List all sessions with optional filtering.</summary>
    public async Task<List<Session>> ListSessionsAsync(bool? leafOnly = null, string? filter = null, CancellationToken ct = default)
    {
        var queryParams = new List<string>();
        if (leafOnly.HasValue) queryParams.Add($"leavesOnly={leafOnly.Value.ToString().ToLower()}");
        if (!string.IsNullOrEmpty(filter)) queryParams.Add($"filter={filter}");

        var url = "/sessions" + (queryParams.Count > 0 ? "?" + string.Join("&", queryParams) : "");
        var response = await _httpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<SessionsResponse>(_jsonOptions, ct)
            ?? throw new InvalidOperationException("Failed to parse sessions response");

        return result.Data;
    }

    /// <summary>Continue a session with a new query (creates a child session).</summary>
    public async Task<ContinueSessionData> ContinueSessionAsync(string sessionId, string query, CancellationToken ct = default)
    {
        var request = new ContinueSessionRequest { Query = query };
        var response = await _httpClient.PostAsJsonAsync($"/sessions/{sessionId}/continue", request, _jsonOptions, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ContinueSessionResponse>(_jsonOptions, ct)
            ?? throw new InvalidOperationException("Failed to parse continue response");

        return result.Data;
    }

    /// <summary>Interrupt a running session.</summary>
    public async Task InterruptSessionAsync(string sessionId, CancellationToken ct = default)
    {
        var response = await _httpClient.PostAsync($"/sessions/{sessionId}/interrupt", null, ct);
        response.EnsureSuccessStatusCode();
        _logger?.LogInformation("Interrupted session: {SessionId}", sessionId);
    }

    /// <summary>Wait for a session to complete.</summary>
    public async Task<Session> WaitForSessionAsync(string sessionId, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (timeout.HasValue) cts.CancelAfter(timeout.Value);

        var completedStatuses = new[] { "completed", "failed", "interrupted" };

        while (!cts.Token.IsCancellationRequested)
        {
            var session = await GetSessionAsync(sessionId, cts.Token);

            if (completedStatuses.Contains(session.Status))
            {
                _logger?.LogInformation("Session {SessionId} finished with status: {Status}", sessionId, session.Status);
                return session;
            }

            _logger?.LogDebug("Session {SessionId} status: {Status}", sessionId, session.Status);
            await Task.Delay(1000, cts.Token);
        }

        throw new OperationCanceledException("Session wait was cancelled");
    }

    /// <summary>Get conversation messages for a session.</summary>
    public async Task<List<ConversationEvent>> GetConversationAsync(string sessionId, CancellationToken ct = default)
    {
        var response = await _httpClient.GetAsync($"/sessions/{sessionId}/messages", ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ConversationResponse>(_jsonOptions, ct)
            ?? throw new InvalidOperationException("Failed to parse conversation response");

        return result.Data;
    }

    // ========================================================================
    // Approval Management
    // ========================================================================

    /// <summary>List all pending approvals.</summary>
    public async Task<List<Approval>> ListApprovalsAsync(string? sessionId = null, CancellationToken ct = default)
    {
        var url = "/approvals" + (sessionId != null ? $"?sessionId={sessionId}" : "");
        var response = await _httpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ApprovalsResponse>(_jsonOptions, ct)
            ?? throw new InvalidOperationException("Failed to parse approvals response");

        return result.Data;
    }

    /// <summary>Get pending approvals only.</summary>
    public async Task<List<Approval>> GetPendingApprovalsAsync(string? sessionId = null, CancellationToken ct = default)
    {
        var approvals = await ListApprovalsAsync(sessionId, ct);
        return approvals.Where(a => a.Status == "pending").ToList();
    }

    /// <summary>Get approval details by ID.</summary>
    public async Task<Approval> GetApprovalAsync(string approvalId, CancellationToken ct = default)
    {
        var response = await _httpClient.GetAsync($"/approvals/{approvalId}", ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ApprovalResponse>(_jsonOptions, ct)
            ?? throw new InvalidOperationException("Failed to parse approval response");

        return result.Data;
    }

    /// <summary>Approve a pending approval request.</summary>
    public async Task<bool> ApproveAsync(string approvalId, string? comment = null, CancellationToken ct = default)
    {
        return await DecideApprovalAsync(approvalId, "approve", comment, ct);
    }

    /// <summary>Deny a pending approval request.</summary>
    public async Task<bool> DenyAsync(string approvalId, string comment, CancellationToken ct = default)
    {
        return await DecideApprovalAsync(approvalId, "deny", comment, ct);
    }

    /// <summary>Make a decision on an approval request.</summary>
    public async Task<bool> DecideApprovalAsync(string approvalId, string decision, string? comment = null, CancellationToken ct = default)
    {
        var request = new DecideApprovalRequest { Decision = decision, Comment = comment };
        var response = await _httpClient.PostAsJsonAsync($"/approvals/{approvalId}/decide", request, _jsonOptions, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<DecideApprovalResponse>(_jsonOptions, ct)
            ?? throw new InvalidOperationException("Failed to parse decision response");

        _logger?.LogInformation("Approval {ApprovalId}: {Decision}", approvalId, decision);
        return result.Data.Success;
    }

    // ========================================================================
    // Server-Sent Events (SSE) Streaming
    // ========================================================================

    /// <summary>
    /// Subscribe to real-time events from the daemon.
    /// </summary>
    public async Task SubscribeToEventsAsync(
        Action<SseEvent> onEvent,
        IEnumerable<string>? eventTypes = null,
        string? sessionId = null,
        CancellationToken ct = default)
    {
        _sseCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var queryParams = new List<string>();
        if (eventTypes != null)
        {
            foreach (var et in eventTypes)
                queryParams.Add($"eventTypes={et}");
        }
        if (sessionId != null) queryParams.Add($"sessionId={sessionId}");

        var url = "/stream/events" + (queryParams.Count > 0 ? "?" + string.Join("&", queryParams) : "");

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, _sseCts.Token);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(_sseCts.Token);
        using var reader = new StreamReader(stream);

        _logger?.LogInformation("Connected to SSE stream");

        while (!_sseCts.Token.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(_sseCts.Token);
            if (line == null) break;

            if (line.StartsWith("data: "))
            {
                var json = line[6..];
                try
                {
                    var evt = JsonSerializer.Deserialize<SseEvent>(json, _jsonOptions);
                    if (evt != null)
                    {
                        onEvent(evt);
                    }
                }
                catch (JsonException ex)
                {
                    _logger?.LogWarning("Failed to parse SSE event: {Message}", ex.Message);
                }
            }
        }
    }

    /// <summary>Stop the SSE subscription.</summary>
    public void StopEventSubscription()
    {
        _sseCts?.Cancel();
        _sseCts?.Dispose();
        _sseCts = null;
    }

    // ========================================================================
    // Convenience Methods for Automation
    // ========================================================================

    /// <summary>
    /// Launch a session and wait for completion, optionally with auto-approve.
    /// </summary>
    public async Task<Session> LaunchAndWaitAsync(
        CreateSessionRequest request,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        var sessionData = await CreateSessionAsync(request, ct);
        return await WaitForSessionAsync(sessionData.SessionId, timeout, ct);
    }

    /// <summary>
    /// Run an automation task with full configuration.
    /// </summary>
    public async Task<TaskResult> RunTaskAsync(AutomationTask task, CancellationToken ct = default)
    {
        var startTime = DateTime.UtcNow;

        var request = new CreateSessionRequest
        {
            Query = task.Query,
            WorkingDir = task.WorkingDir,
            Model = task.Model,
            MaxTurns = task.MaxTurns,
            AllowedTools = task.AllowedTools,
            DangerouslySkipPermissions = task.AutoApprove,
            DangerouslySkipPermissionsTimeoutMs = task.AutoApproveTimeout.HasValue
                ? (long)task.AutoApproveTimeout.Value.TotalMilliseconds
                : null,
            ProxyEnabled = !string.IsNullOrEmpty(task.ProxyBaseUrl),
            ProxyBaseUrl = task.ProxyBaseUrl,
            ProxyModelOverride = task.ProxyModel,
            ProxyApiKey = task.ProxyApiKey
        };

        try
        {
            var sessionData = await CreateSessionAsync(request, ct);
            var session = await WaitForSessionAsync(sessionData.SessionId, ct: ct);

            return new TaskResult
            {
                TaskName = task.Name,
                SessionId = session.Id,
                Status = session.Status,
                Duration = DateTime.UtcNow - startTime,
                CostUsd = session.CostUsd,
                InputTokens = session.InputTokens,
                OutputTokens = session.OutputTokens,
                Summary = session.Summary,
                ErrorMessage = session.ErrorMessage
            };
        }
        catch (Exception ex)
        {
            return new TaskResult
            {
                TaskName = task.Name,
                SessionId = "",
                Status = "error",
                Duration = DateTime.UtcNow - startTime,
                ErrorMessage = ex.Message
            };
        }
    }

    public void Dispose()
    {
        StopEventSubscription();
        _httpClient.Dispose();
        GC.SuppressFinalize(this);
    }
}
