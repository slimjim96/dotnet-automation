using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

var store = new TodoStore("todos.json");

if (args.Length == 0)
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  todo add \"task\" [--priority high|medium|low]");
    Console.WriteLine("  todo list [--all|--pending|--completed]");
    Console.WriteLine("  todo complete <id>");
    Console.WriteLine("  todo delete <id>");
    return;
}

var command = args[0].ToLower();

switch (command)
{
    case "add":
        if (args.Length < 2)
        {
            Console.WriteLine("Error: Task description is required.");
            return;
        }

        var taskDescription = args[1];
        var priority = Priority.Medium;

        if (args.Length > 2 && args[2] == "--priority")
        {
            if (args.Length > 3 && Enum.TryParse<Priority>(args[3], true, out var parsedPriority))
            {
                priority = parsedPriority;
            }
            else
            {
                Console.WriteLine("Error: Invalid priority. Use high, medium, or low.");
                return;
            }
        }

        var newItem = new TodoItem
        {
            Id = store.GetNextId(),
            Title = taskDescription,
            Priority = priority,
            IsCompleted = false,
            CreatedAt = DateTime.UtcNow
        };
        store.Add(newItem);
        Console.WriteLine($"Added: {newItem.Title} (Priority: {newItem.Priority})");
        break;

    case "list":
        var filter = args.Length > 1 ? args[1].ToLower() : "--all";
        var todos = filter switch
        {
            "--all" => store.GetAll(),
            "--pending" => store.GetPending(),
            "--completed" => store.GetCompleted(),
            _ => null
        };

        if (todos == null)
        {
            Console.WriteLine("Error: Invalid filter. Use --all, --pending, or --completed.");
            return;
        }

        foreach (var todo in todos)
        {
            var status = todo.IsCompleted ? "✔" : "✗";
            Console.WriteLine($"[{status}] {todo.Id}: {todo.Title} (Priority: {todo.Priority})");
        }
        break;

    case "complete":
        if (args.Length < 2 || !int.TryParse(args[1], out var completeId))
        {
            Console.WriteLine("Error: Valid ID is required.");
            return;
        }

        if (store.Complete(completeId))
        {
            Console.WriteLine($"Marked task {completeId} as completed.");
        }
        else
        {
            Console.WriteLine($"Error: Task with ID {completeId} not found.");
        }
        break;

    case "delete":
        if (args.Length < 2 || !int.TryParse(args[1], out var deleteId))
        {
            Console.WriteLine("Error: Valid ID is required.");
            return;
        }

        if (store.Delete(deleteId))
        {
            Console.WriteLine($"Deleted task {deleteId}.");
        }
        else
        {
            Console.WriteLine($"Error: Task with ID {deleteId} not found.");
        }
        break;

    default:
        Console.WriteLine("Error: Unknown command.");
        break;
}

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
        return JsonSerializer.Deserialize<List<TodoItem>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        }) ?? new List<TodoItem>();
    }

    private void Save()
    {
        var json = JsonSerializer.Serialize(_items, new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        });
        File.WriteAllText(_filePath, json);
    }
}
