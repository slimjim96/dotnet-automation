using System.Text.Json;
using System.Text.Json.Serialization;

namespace HumanLayerAutomation;

/// <summary>
/// JSON-serializable task configuration for the auto builder.
/// Can be used by CLI, web UI, or other interfaces.
/// </summary>
public class TaskConfig
{
    /// <summary>Unique task identifier (auto-generated if not provided).</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    /// <summary>Human-readable task name.</summary>
    public string Name { get; set; } = "";

    /// <summary>Task description - what to build/update.</summary>
    public string Description { get; set; } = "";

    /// <summary>Target directory path.</summary>
    public string TargetPath { get; set; } = "";

    /// <summary>Build scenario: NewRepo, UpdateRepo, AddToRepo.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public BuildScenario Scenario { get; set; } = BuildScenario.NewRepo;

    /// <summary>Decision strategy: FirstOption, Efficiency, Thorough, Balanced.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public DecisionStrategy Strategy { get; set; } = DecisionStrategy.Balanced;

    /// <summary>Build scope: SingleComponent, CoreOnly, FullBuild.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public BuildScope Scope { get; set; } = BuildScope.FullBuild;

    /// <summary>Component to focus on (when Scope is SingleComponent).</summary>
    public string? FocusComponent { get; set; }

    /// <summary>Model to use: haiku, sonnet, opus.</summary>
    public string Model { get; set; } = "sonnet";

    /// <summary>Maximum cost in USD (0 = unlimited).</summary>
    public decimal MaxCostUsd { get; set; } = 0;

    /// <summary>Maximum retries per step.</summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>Maximum build fix attempts.</summary>
    public int MaxBuildFixes { get; set; } = 3;

    /// <summary>Run fully autonomous without pauses.</summary>
    public bool Autonomous { get; set; } = true;

    /// <summary>Additional context or instructions.</summary>
    public string? AdditionalContext { get; set; }

    /// <summary>Tags for organization/filtering.</summary>
    public List<string> Tags { get; set; } = [];

    /// <summary>When the task was created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Convert to BuildConfig for execution.</summary>
    public BuildConfig ToBuildConfig() => new()
    {
        Scenario = Scenario,
        Strategy = Strategy,
        Scope = Scope,
        TargetPath = TargetPath,
        Description = Description + (string.IsNullOrEmpty(AdditionalContext) ? "" : $"\n\nAdditional context: {AdditionalContext}"),
        FocusComponent = FocusComponent,
        Model = Model,
        MaxCostUsd = MaxCostUsd,
        MaxRetries = MaxRetries,
        MaxBuildFixes = MaxBuildFixes,
        FullyAutonomous = Autonomous
    };

    /// <summary>Create from BuildConfig.</summary>
    public static TaskConfig FromBuildConfig(BuildConfig config, string? name = null) => new()
    {
        Name = name ?? $"Task-{DateTime.Now:yyyyMMdd-HHmmss}",
        Description = config.Description,
        TargetPath = config.TargetPath,
        Scenario = config.Scenario,
        Strategy = config.Strategy,
        Scope = config.Scope,
        FocusComponent = config.FocusComponent,
        Model = config.Model,
        MaxCostUsd = config.MaxCostUsd,
        MaxRetries = config.MaxRetries,
        MaxBuildFixes = config.MaxBuildFixes,
        Autonomous = config.FullyAutonomous
    };
}

/// <summary>
/// Tracks the status of a running or completed task.
/// </summary>
public class TaskStatus
{
    public string TaskId { get; set; } = "";
    public string Name { get; set; } = "";

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TaskState State { get; set; } = TaskState.Pending;

    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public TimeSpan? Duration => CompletedAt.HasValue && StartedAt.HasValue
        ? CompletedAt.Value - StartedAt.Value
        : StartedAt.HasValue ? DateTime.UtcNow - StartedAt.Value : null;

    public decimal CostUsd { get; set; }
    public int StepsCompleted { get; set; }
    public int TotalSteps { get; set; }
    public string? CurrentStep { get; set; }
    public string? ErrorMessage { get; set; }
    public bool Success { get; set; }

    public List<string> Log { get; set; } = [];
}

public enum TaskState
{
    Pending,
    Running,
    Completed,
    Failed,
    Cancelled
}

/// <summary>
/// Registry for tracking multiple tasks.
/// Persists to JSON file for durability.
/// </summary>
public class TaskRegistry
{
    private readonly string _registryPath;
    private readonly object _lock = new();
    private RegistryData _data;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public TaskRegistry(string? registryPath = null)
    {
        _registryPath = registryPath ?? GetDefaultRegistryPath();
        _data = Load();
    }

    /// <summary>Get all task configurations.</summary>
    public IReadOnlyList<TaskConfig> GetConfigs() => _data.Configs.AsReadOnly();

    /// <summary>Get all task statuses.</summary>
    public IReadOnlyList<TaskStatus> GetStatuses() => _data.Statuses.AsReadOnly();

    /// <summary>Get a specific task config by ID.</summary>
    public TaskConfig? GetConfig(string taskId) =>
        _data.Configs.FirstOrDefault(c => c.Id == taskId);

    /// <summary>Get a specific task status by ID.</summary>
    public TaskStatus? GetStatus(string taskId) =>
        _data.Statuses.FirstOrDefault(s => s.TaskId == taskId);

    /// <summary>Add a new task configuration.</summary>
    public void AddConfig(TaskConfig config)
    {
        lock (_lock)
        {
            // Remove existing with same ID
            _data.Configs.RemoveAll(c => c.Id == config.Id);
            _data.Configs.Add(config);
            Save();
        }
    }

    /// <summary>Update task status.</summary>
    public void UpdateStatus(TaskStatus status)
    {
        lock (_lock)
        {
            var existing = _data.Statuses.FindIndex(s => s.TaskId == status.TaskId);
            if (existing >= 0)
                _data.Statuses[existing] = status;
            else
                _data.Statuses.Add(status);
            Save();
        }
    }

    /// <summary>Remove a task (config and status).</summary>
    public void RemoveTask(string taskId)
    {
        lock (_lock)
        {
            _data.Configs.RemoveAll(c => c.Id == taskId);
            _data.Statuses.RemoveAll(s => s.TaskId == taskId);
            Save();
        }
    }

    /// <summary>Get running tasks.</summary>
    public IReadOnlyList<TaskStatus> GetRunningTasks() =>
        _data.Statuses.Where(s => s.State == TaskState.Running).ToList().AsReadOnly();

    /// <summary>Get recent tasks (last N).</summary>
    public IReadOnlyList<TaskStatus> GetRecentTasks(int count = 10) =>
        _data.Statuses
            .OrderByDescending(s => s.StartedAt ?? DateTime.MinValue)
            .Take(count)
            .ToList()
            .AsReadOnly();

    private RegistryData Load()
    {
        if (!File.Exists(_registryPath))
            return new RegistryData();

        try
        {
            var json = File.ReadAllText(_registryPath);
            return JsonSerializer.Deserialize<RegistryData>(json, JsonOptions) ?? new RegistryData();
        }
        catch
        {
            return new RegistryData();
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

    private static string GetDefaultRegistryPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "dotnet-automation", "task-registry.json");
    }

    private class RegistryData
    {
        public List<TaskConfig> Configs { get; set; } = [];
        public List<TaskStatus> Statuses { get; set; } = [];
    }
}

/// <summary>
/// Helper for loading/saving individual task configs.
/// </summary>
public static class TaskConfigLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>Load a task config from a JSON file.</summary>
    public static TaskConfig Load(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<TaskConfig>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Failed to parse task config from {path}");
    }

    /// <summary>Save a task config to a JSON file.</summary>
    public static void Save(TaskConfig config, string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(path, json);
    }

    /// <summary>Create a sample task config JSON.</summary>
    public static string CreateSampleJson() => JsonSerializer.Serialize(new TaskConfig
    {
        Id = "sample01",
        Name = "Build Todo App",
        Description = "A command-line Todo application with add, list, complete, delete commands",
        TargetPath = "./output/TodoApp",
        Scenario = BuildScenario.NewRepo,
        Strategy = DecisionStrategy.Balanced,
        Scope = BuildScope.FullBuild,
        Model = "sonnet",
        MaxRetries = 3,
        MaxBuildFixes = 3,
        Autonomous = true,
        Tags = ["demo", "cli", "todo"],
        AdditionalContext = "Store data in JSON file. Include priority levels."
    }, JsonOptions);
}
