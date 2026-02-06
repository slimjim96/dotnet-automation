namespace HumanLayerAutomation.Providers;

/// <summary>
/// Interface for AI code generation providers.
/// </summary>
public interface ICodeProvider
{
    /// <summary>Provider identifier.</summary>
    string ProviderId { get; }

    /// <summary>Check if the provider is available.</summary>
    Task<bool> IsAvailableAsync(CancellationToken ct = default);

    /// <summary>Run a prompt and get the response.</summary>
    Task<ProviderResult> RunAsync(ProviderRequest request, CancellationToken ct = default);

    /// <summary>Check remaining quota/tokens (if applicable).</summary>
    Task<QuotaInfo?> GetQuotaAsync(CancellationToken ct = default);
}

/// <summary>
/// Request to a code provider.
/// </summary>
public class ProviderRequest
{
    public required string Prompt { get; set; }
    public string? SystemPrompt { get; set; }
    public string? Model { get; set; }
    public string? WorkingDirectory { get; set; }
    public int? MaxTokens { get; set; }
    public bool AutoApprove { get; set; } = true;
    public List<string>? AllowedTools { get; set; }
}

/// <summary>
/// Result from a code provider.
/// </summary>
public class ProviderResult
{
    public bool Success { get; set; }
    public string? Output { get; set; }
    public string? Error { get; set; }
    public string ProviderId { get; set; } = "";
    public string? ModelId { get; set; }
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public decimal Cost { get; set; }
    public TimeSpan Duration { get; set; }
    public bool QuotaExhausted { get; set; }
}

/// <summary>
/// Quota information for a provider.
/// </summary>
public class QuotaInfo
{
    public int? RemainingTokens { get; set; }
    public int? RemainingRequests { get; set; }
    public DateTime? ResetsAt { get; set; }
    public bool IsUnlimited { get; set; }
    public decimal? RemainingBudget { get; set; }
}
