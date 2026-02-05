using System.Text.Json;

public enum Priority
{
    Low = 0,
    Medium = 1,
    High = 2
}

public record TodoItem
{
    public int Id { get; init; }
    public string Task { get; init; } = "";
    public bool IsCompleted { get; set; }
    public Priority Priority { get; init; }
    public DateTime CreatedAt { get; init; }
}

public class TodoStore
{
    private const string FilePath = "todos.json";
    private List<TodoItem> _items;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public TodoStore()
    {
        _items = Load();
    }

    private List<TodoItem> Load()
    {
        if (!File.Exists(FilePath))
            return new List<TodoItem>();

        try
        {
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<List<TodoItem>>(json, JsonOptions) ?? new List<TodoItem>();
        }
        catch
        {
            return new List<TodoItem>();
        }
    }

    private void Save()
    {
        var json = JsonSerializer.Serialize(_items, JsonOptions);
        File.WriteAllText(FilePath, json);
    }

    public void Add(string task, Priority priority)
    {
        var nextId = _items.Count > 0 ? _items.Max(t => t.Id) + 1 : 1;
        var item = new TodoItem
        {
            Id = nextId,
            Task = task,
            Priority = priority,
            IsCompleted = false,
            CreatedAt = DateTime.Now
        };
        _items.Add(item);
        Save();
    }

    public List<TodoItem> GetAll() => _items.ToList();
    public List<TodoItem> GetPending() => _items.Where(t => !t.IsCompleted).ToList();
    public List<TodoItem> GetCompleted() => _items.Where(t => t.IsCompleted).ToList();

    public bool Complete(int id)
    {
        var item = _items.FirstOrDefault(t => t.Id == id);
        if (item == null) return false;
        item.IsCompleted = true;
        Save();
        return true;
    }

    public bool Delete(int id)
    {
        var item = _items.FirstOrDefault(t => t.Id == id);
        if (item == null) return false;
        _items.Remove(item);
        Save();
        return true;
    }
}
