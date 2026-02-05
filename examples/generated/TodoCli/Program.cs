using System;
using System.Linq;

if (args.Length == 0)
{
    PrintHelp();
    return;
}

var store = new TodoStore();
var command = args[0].ToLower();

switch (command)
{
    case "add":
        HandleAdd(args, store);
        break;
    case "list":
        HandleList(args, store);
        break;
    case "complete":
        HandleComplete(args, store);
        break;
    case "delete":
        HandleDelete(args, store);
        break;
    default:
        Console.WriteLine($"Unknown command: {command}");
        PrintHelp();
        break;
}

static void HandleAdd(string[] args, TodoStore store)
{
    if (args.Length < 2)
    {
        Console.WriteLine("Error: Task description required");
        Console.WriteLine("Usage: todo add <task> [--priority high|medium|low]");
        return;
    }

    var priority = Priority.Medium;
    var taskParts = new List<string>();

    for (int i = 1; i < args.Length; i++)
    {
        if (args[i] == "--priority" && i + 1 < args.Length)
        {
            var priorityArg = args[i + 1].ToLower();
            priority = priorityArg switch
            {
                "high" => Priority.High,
                "low" => Priority.Low,
                _ => Priority.Medium
            };
            i++;
        }
        else
        {
            taskParts.Add(args[i]);
        }
    }

    var task = string.Join(" ", taskParts);
    if (string.IsNullOrWhiteSpace(task))
    {
        Console.WriteLine("Error: Task description cannot be empty");
        return;
    }

    store.Add(task, priority);
    Console.WriteLine($"Added task: {task} (Priority: {priority})");
}

static void HandleList(string[] args, TodoStore store)
{
    var filter = args.Length > 1 ? args[1].ToLower() : "--pending";

    List<TodoItem> todos = filter switch
    {
        "--all" => store.GetAll(),
        "--completed" => store.GetCompleted(),
        _ => store.GetPending()
    };

    if (!todos.Any())
    {
        Console.WriteLine("No tasks found.");
        return;
    }

    var filterName = filter switch
    {
        "--all" => "All",
        "--completed" => "Completed",
        _ => "Pending"
    };

    Console.WriteLine($"\n{filterName} Tasks:");
    Console.WriteLine(new string('-', 60));

    foreach (var todo in todos.OrderBy(t => t.IsCompleted).ThenByDescending(t => t.Priority).ThenBy(t => t.CreatedAt))
    {
        var status = todo.IsCompleted ? "x" : " ";
        var priorityIcon = todo.Priority switch
        {
            Priority.High => "!!!",
            Priority.Medium => "!! ",
            Priority.Low => "!  ",
            _ => "   "
        };

        Console.WriteLine($"[{status}] {todo.Id,3} | {priorityIcon} | {todo.Task}");
    }

    Console.WriteLine(new string('-', 60));
    Console.WriteLine($"Total: {todos.Count} task(s)\n");
}

static void HandleComplete(string[] args, TodoStore store)
{
    if (args.Length < 2 || !int.TryParse(args[1], out int id))
    {
        Console.WriteLine("Error: Valid task ID required");
        Console.WriteLine("Usage: todo complete <id>");
        return;
    }

    if (store.Complete(id))
    {
        Console.WriteLine($"Task {id} marked as completed");
    }
    else
    {
        Console.WriteLine($"Error: Task {id} not found");
    }
}

static void HandleDelete(string[] args, TodoStore store)
{
    if (args.Length < 2 || !int.TryParse(args[1], out int id))
    {
        Console.WriteLine("Error: Valid task ID required");
        Console.WriteLine("Usage: todo delete <id>");
        return;
    }

    if (store.Delete(id))
    {
        Console.WriteLine($"Task {id} deleted");
    }
    else
    {
        Console.WriteLine($"Error: Task {id} not found");
    }
}

static void PrintHelp()
{
    Console.WriteLine("Todo CLI - Task Management");
    Console.WriteLine("\nCommands:");
    Console.WriteLine("  todo add <task> [--priority high|medium|low]");
    Console.WriteLine("  todo list [--all|--pending|--completed]");
    Console.WriteLine("  todo complete <id>");
    Console.WriteLine("  todo delete <id>");
    Console.WriteLine("\nExamples:");
    Console.WriteLine("  todo add \"Buy groceries\" --priority high");
    Console.WriteLine("  todo list --pending");
    Console.WriteLine("  todo complete 1");
}
