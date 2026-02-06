using System.Text.Json;
using System.Text.Json.Serialization;

namespace HumanLayerAutomation.Models;

/// <summary>
/// Registry for AI models with cost tracking and capability metadata.
/// Supports multiple providers (Claude, GitHub Copilot, etc.)
/// </summary>
public class ModelRegistry
{
    private readonly string _registryPath;
    private ModelRegistryData _data;
    private static readonly object _lock = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public ModelRegistry(string? registryPath = null)
    {
        _registryPath = registryPath ?? GetDefaultPath();
        _data = Load();

        // Initialize with defaults if empty
        if (_data.Models.Count == 0)
        {
            InitializeDefaults();
        }
    }

    /// <summary>Get all registered models.</summary>
    public IReadOnlyList<ModelInfo> GetAllModels() => _data.Models.AsReadOnly();

    /// <summary>Get models by provider.</summary>
    public IReadOnlyList<ModelInfo> GetModelsByProvider(string provider) =>
        _data.Models.Where(m => m.Provider.Equals(provider, StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>Get models suitable for coding tasks, ordered by priority.</summary>
    public IReadOnlyList<ModelInfo> GetCodingModels() =>
        _data.Models
            .Where(m => m.Capabilities.Contains("coding", StringComparer.OrdinalIgnoreCase) && m.Enabled)
            .OrderByDescending(m => m.CodingPriority)
            .ThenBy(m => m.CostPerMillionOutput)
            .ToList();

    /// <summary>Get the best available model for coding.</summary>
    public ModelInfo? GetBestCodingModel() => GetCodingModels().FirstOrDefault();

    /// <summary>Get a specific model by ID.</summary>
    public ModelInfo? GetModel(string modelId) =>
        _data.Models.FirstOrDefault(m => m.Id.Equals(modelId, StringComparison.OrdinalIgnoreCase));

    /// <summary>Get model by alias (e.g., "sonnet" -> "claude-sonnet-4-20250514").</summary>
    public ModelInfo? GetModelByAlias(string alias)
    {
        // Check direct ID match first
        var direct = GetModel(alias);
        if (direct != null) return direct;

        // Check aliases
        return _data.Models.FirstOrDefault(m =>
            m.Aliases.Contains(alias, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>Add or update a model.</summary>
    public void UpsertModel(ModelInfo model)
    {
        lock (_lock)
        {
            var existing = _data.Models.FindIndex(m => m.Id.Equals(model.Id, StringComparison.OrdinalIgnoreCase));
            if (existing >= 0)
                _data.Models[existing] = model;
            else
                _data.Models.Add(model);
            Save();
        }
    }

    /// <summary>Remove a model.</summary>
    public void RemoveModel(string modelId)
    {
        lock (_lock)
        {
            _data.Models.RemoveAll(m => m.Id.Equals(modelId, StringComparison.OrdinalIgnoreCase));
            Save();
        }
    }

    /// <summary>Enable/disable a model.</summary>
    public void SetModelEnabled(string modelId, bool enabled)
    {
        var model = GetModel(modelId);
        if (model != null)
        {
            model.Enabled = enabled;
            UpsertModel(model);
        }
    }

    /// <summary>Update model pricing.</summary>
    public void UpdatePricing(string modelId, decimal inputCost, decimal outputCost)
    {
        var model = GetModel(modelId);
        if (model != null)
        {
            model.CostPerMillionInput = inputCost;
            model.CostPerMillionOutput = outputCost;
            model.LastUpdated = DateTime.UtcNow;
            UpsertModel(model);
        }
    }

    /// <summary>Record usage for cost tracking.</summary>
    public void RecordUsage(string modelId, int inputTokens, int outputTokens, decimal? costOverride = null)
    {
        lock (_lock)
        {
            var model = GetModel(modelId);
            decimal cost = costOverride ?? CalculateCost(modelId, inputTokens, outputTokens);

            var usage = new ModelUsageRecord
            {
                ModelId = modelId,
                Timestamp = DateTime.UtcNow,
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                Cost = cost
            };

            _data.UsageHistory.Add(usage);

            // Keep only last 1000 records
            if (_data.UsageHistory.Count > 1000)
            {
                _data.UsageHistory = _data.UsageHistory.TakeLast(1000).ToList();
            }

            Save();
        }
    }

    /// <summary>Calculate cost for a given usage.</summary>
    public decimal CalculateCost(string modelId, int inputTokens, int outputTokens)
    {
        var model = GetModel(modelId);
        if (model == null) return 0;

        var inputCost = (inputTokens / 1_000_000m) * model.CostPerMillionInput;
        var outputCost = (outputTokens / 1_000_000m) * model.CostPerMillionOutput;
        return inputCost + outputCost;
    }

    /// <summary>Get total cost for a time period.</summary>
    public decimal GetTotalCost(DateTime? since = null)
    {
        var records = since.HasValue
            ? _data.UsageHistory.Where(u => u.Timestamp >= since.Value)
            : _data.UsageHistory;
        return records.Sum(u => u.Cost);
    }

    /// <summary>Get usage summary by model.</summary>
    public Dictionary<string, ModelUsageSummary> GetUsageSummary(DateTime? since = null)
    {
        var records = since.HasValue
            ? _data.UsageHistory.Where(u => u.Timestamp >= since.Value)
            : _data.UsageHistory;

        return records
            .GroupBy(u => u.ModelId)
            .ToDictionary(
                g => g.Key,
                g => new ModelUsageSummary
                {
                    ModelId = g.Key,
                    TotalInputTokens = g.Sum(u => u.InputTokens),
                    TotalOutputTokens = g.Sum(u => u.OutputTokens),
                    TotalCost = g.Sum(u => u.Cost),
                    RequestCount = g.Count()
                });
    }

    /// <summary>Get the fallback chain for a model.</summary>
    public IReadOnlyList<ModelInfo> GetFallbackChain(string modelId)
    {
        var primary = GetModelByAlias(modelId);
        if (primary == null) return [];

        var chain = new List<ModelInfo> { primary };

        // Add fallbacks based on provider priority
        var codingModels = GetCodingModels()
            .Where(m => m.Id != primary.Id && m.Enabled)
            .ToList();

        chain.AddRange(codingModels);
        return chain;
    }

    private void InitializeDefaults()
    {
        // Claude models (as of early 2025)
        _data.Models.AddRange([
            new ModelInfo
            {
                Id = "claude-opus-4-20250514",
                Provider = "anthropic",
                DisplayName = "Claude Opus 4",
                Aliases = ["opus", "claude-opus", "opus-4"],
                CostPerMillionInput = 15.00m,
                CostPerMillionOutput = 75.00m,
                ContextWindow = 200000,
                MaxOutput = 32000,
                Capabilities = ["coding", "analysis", "reasoning", "creative"],
                CodingPriority = 100,
                Notes = "Most capable, best for complex coding tasks"
            },
            new ModelInfo
            {
                Id = "claude-sonnet-4-20250514",
                Provider = "anthropic",
                DisplayName = "Claude Sonnet 4",
                Aliases = ["sonnet", "claude-sonnet", "sonnet-4"],
                CostPerMillionInput = 3.00m,
                CostPerMillionOutput = 15.00m,
                ContextWindow = 200000,
                MaxOutput = 64000,
                Capabilities = ["coding", "analysis", "reasoning"],
                CodingPriority = 90,
                Notes = "Best balance of capability and cost for coding"
            },
            new ModelInfo
            {
                Id = "claude-haiku-3-5-20241022",
                Provider = "anthropic",
                DisplayName = "Claude Haiku 3.5",
                Aliases = ["haiku", "claude-haiku", "haiku-3.5"],
                CostPerMillionInput = 0.80m,
                CostPerMillionOutput = 4.00m,
                ContextWindow = 200000,
                MaxOutput = 8192,
                Capabilities = ["coding", "analysis", "fast"],
                CodingPriority = 70,
                Notes = "Fast and cheap, good for simple tasks"
            },
            // GitHub Copilot (via GitHub Models API — requires Copilot subscription)
            // Uses publisher/model_name format. See: https://models.github.ai/catalog/models
            new ModelInfo
            {
                Id = "openai/gpt-4o",
                Provider = "github",
                DisplayName = "GitHub Models (GPT-4o)",
                Aliases = ["copilot", "gh-copilot", "github-copilot", "gpt-4o"],
                CostPerMillionInput = 0m, // Subscription-based — no per-token cost
                CostPerMillionOutput = 0m,
                ContextWindow = 128000,
                MaxOutput = 16384,
                Capabilities = ["coding", "analysis", "reasoning", "explain"],
                CodingPriority = 80,
                Notes = "GitHub Pro: 1000 premium prompts/month. Full coding via Models API.",
                RequiresSubscription = true
            },
            new ModelInfo
            {
                Id = "openai/gpt-4o-mini",
                Provider = "github",
                DisplayName = "GitHub Models (GPT-4o-mini)",
                Aliases = ["copilot-mini", "gh-copilot-mini", "gpt-4o-mini"],
                CostPerMillionInput = 0m,
                CostPerMillionOutput = 0m,
                ContextWindow = 128000,
                MaxOutput = 16384,
                Capabilities = ["coding", "analysis", "fast"],
                CodingPriority = 65,
                Notes = "Fast, low-quota-cost option for simpler coding tasks.",
                RequiresSubscription = true
            },
            new ModelInfo
            {
                Id = "openai/o3-mini",
                Provider = "github",
                DisplayName = "GitHub Models (o3-mini)",
                Aliases = ["copilot-o3", "gh-o3-mini", "o3-mini"],
                CostPerMillionInput = 0m,
                CostPerMillionOutput = 0m,
                ContextWindow = 128000,
                MaxOutput = 65536,
                Capabilities = ["coding", "reasoning", "analysis"],
                CodingPriority = 85,
                Notes = "Reasoning model via GitHub Models. Great for complex logic.",
                RequiresSubscription = true
            },
            // OpenAI models (direct API — pay-per-use)
            new ModelInfo
            {
                Id = "openai-gpt-4o",
                Provider = "openai",
                DisplayName = "GPT-4o (OpenAI API)",
                Aliases = ["gpt4o", "gpt-4-omni", "openai-gpt4o"],
                CostPerMillionInput = 2.50m,
                CostPerMillionOutput = 10.00m,
                ContextWindow = 128000,
                MaxOutput = 16384,
                Capabilities = ["coding", "analysis", "vision"],
                CodingPriority = 75,
                Enabled = false, // Disabled by default — enable if OPENAI_API_KEY is set
                Notes = "OpenAI direct API. Pay-per-token. Enable as last-resort fallback."
            }
        ]);

        Save();
    }

    private ModelRegistryData Load()
    {
        if (!File.Exists(_registryPath))
            return new ModelRegistryData();

        try
        {
            var json = File.ReadAllText(_registryPath);
            return JsonSerializer.Deserialize<ModelRegistryData>(json, JsonOptions) ?? new ModelRegistryData();
        }
        catch
        {
            return new ModelRegistryData();
        }
    }

    private void Save()
    {
        var dir = Path.GetDirectoryName(_registryPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(_data, JsonOptions);
        File.WriteAllText(_registryPath, json);
    }

    private static string GetDefaultPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "dotnet-automation", "model-registry.json");
    }

    private class ModelRegistryData
    {
        public List<ModelInfo> Models { get; set; } = [];
        public List<ModelUsageRecord> UsageHistory { get; set; } = [];
    }
}

/// <summary>
/// Information about an AI model.
/// </summary>
public class ModelInfo
{
    public string Id { get; set; } = "";
    public string Provider { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public List<string> Aliases { get; set; } = [];

    // Pricing (per million tokens)
    public decimal CostPerMillionInput { get; set; }
    public decimal CostPerMillionOutput { get; set; }

    // Limits
    public int ContextWindow { get; set; }
    public int MaxOutput { get; set; }

    // Capabilities and priority
    public List<string> Capabilities { get; set; } = [];
    public int CodingPriority { get; set; } // Higher = better for coding

    // Status
    public bool Enabled { get; set; } = true;
    public bool RequiresSubscription { get; set; }
    public string? Notes { get; set; }
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Record of model usage for cost tracking.
/// </summary>
public class ModelUsageRecord
{
    public string ModelId { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public decimal Cost { get; set; }
}

/// <summary>
/// Summary of model usage.
/// </summary>
public class ModelUsageSummary
{
    public string ModelId { get; set; } = "";
    public int TotalInputTokens { get; set; }
    public int TotalOutputTokens { get; set; }
    public decimal TotalCost { get; set; }
    public int RequestCount { get; set; }
}
