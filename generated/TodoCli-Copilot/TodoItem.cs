using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

public enum Priority
{
    Low = 0,
    Medium = 1,
    High = 2
}

public record TodoItem
{
    public int Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public bool IsCompleted { get; init; }
    public Priority Priority { get; init; }
    public DateTime CreatedAt { get; init; }
}

public class TodoStore
{
    private readonly string _filePath;
    private readonly List<TodoItem> _items;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public TodoStore(string filePath)
    {
        _filePath = filePath;
        _items = Load();
    }

    public void Add(TodoItem item)
    {
        _items.Add(item);
        Save();
    }

    public List<TodoItem> GetAll() => _items;

    public List<TodoItem> GetPending() => _items.Where(item => !item.IsCompleted).ToList();

    public List<TodoItem> GetCompleted() => _items.Where(item => item.IsCompleted).ToList();

    public bool Complete(int id)
    {
        var item = _items.FirstOrDefault(i => i.Id == id);
        if (item == null) return false;

        var updatedItem = item with { IsCompleted = true };
        _items[_items.IndexOf(item)] = updatedItem;
        Save();
        return true;
    }

    public bool Delete(int id)
    {
        var item = _items.FirstOrDefault(i => i.Id == id);
        if (item == null) return false;

        _items.Remove(item);
        Save();
        return true;
    }

    public int GetNextId() => _items.Count == 0 ? 1 : _items.Max(item => item.Id) + 1;

    private List<TodoItem> Load()
    {
        if (!File.Exists(_filePath))
            return new List<TodoItem>();

        var json = File.ReadAllText(_filePath);
        return JsonSerializer.Deserialize<List<TodoItem>>(json, JsonOptions) ?? new List<TodoItem>();
    }

    private void Save()
    {
        var json = JsonSerializer.Serialize(_items, JsonOptions);
        File.WriteAllText(_filePath, json);
    }
}
