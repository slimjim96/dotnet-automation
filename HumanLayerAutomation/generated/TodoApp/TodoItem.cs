using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TodoApp.Models;

public enum Priority
{
    Low = 0,
    Medium = 1,
    High = 2
}

public record TodoItem(
    int Id,
    string Title,
    bool IsCompleted,
    Priority Priority,
    DateTime CreatedAt
);

public class TodoStore
{
    private const string FileName = "todos.json";
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public List<TodoItem> Load()
    {
        if (!File.Exists(FileName))
        {
            return new List<TodoItem>();
        }

        try
        {
            string json = File.ReadAllText(FileName);
            return JsonSerializer.Deserialize<List<TodoItem>>(json, _jsonOptions) ?? new List<TodoItem>();
        }
        catch
        {
            return new List<TodoItem>();
        }
    }

    public void Save(List<TodoItem> todos)
    {
        string json = JsonSerializer.Serialize(todos, _jsonOptions);
        File.WriteAllText(FileName, json);
    }

    public TodoItem Add(string title, Priority priority)
    {
        var todos = Load();
        int newId = todos.Any() ? todos.Max(t => t.Id) + 1 : 1;
        var newTodo = new TodoItem(newId, title, false, priority, DateTime.Now);
        todos.Add(newTodo);
        Save(todos);
        return newTodo;
    }

    public List<TodoItem> GetAll()
    {
        return Load();
    }

    public List<TodoItem> GetPending()
    {
        return Load().Where(t => !t.IsCompleted).ToList();
    }

    public List<TodoItem> GetCompleted()
    {
        return Load().Where(t => t.IsCompleted).ToList();
    }

    public bool Complete(int id)
    {
        var todos = Load();
        var todo = todos.FirstOrDefault(t => t.Id == id);
        if (todo == null)
        {
            return false;
        }

        var index = todos.IndexOf(todo);
        todos[index] = todo with { IsCompleted = true };
        Save(todos);
        return true;
    }

    public bool Delete(int id)
    {
        var todos = Load();
        var todo = todos.FirstOrDefault(t => t.Id == id);
        if (todo == null)
        {
            return false;
        }

        todos.Remove(todo);
        Save(todos);
        return true;
    }
}