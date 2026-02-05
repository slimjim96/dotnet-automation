using System;
using System.Linq;
using TodoApp.Models;

if (args.Length == 0)
{
    ShowHelp();
    return;
}

var store = new TodoStore();

var command = args[0].ToLower();

switch (command)
{
    case "add":
        if (args.Length < 2)
        {
            Console.WriteLine("Error: Please provide a task title");
            Console.WriteLine("Usage: dotnet run add <title> [--priority high|medium|low]");
            return;
        }

        var title = args[1];
        var priority = Priority.Medium;

        if (args.Length > 2 && args[2] == "--priority" && args.Length > 3)
        {
            if (Enum.TryParse<Priority>(args[3], true, out var parsedPriority))
            {
                priority = parsedPriority;
            }
        }

        var newItem = store.Add(title, priority);
        Console.WriteLine($"Added task #{newItem.Id}: {newItem.Title} (Priority: {newItem.Priority})");
        break;

    case "list":
        var filter = args.Length > 1 ? args[1].ToLower() : "all";
        var items = filter switch
        {
            "pending" => store.GetPending(),
            "completed" => store.GetCompleted(),
            _ => store.GetAll()
        };

        if (!items.Any())
        {
            Console.WriteLine("No tasks found.");
            return;
        }

        Console.WriteLine($"\n{filter.ToUpper()} TASKS:");
        Console.WriteLine(new string('-', 60));

        foreach (var item in items)
        {
            var checkbox = item.IsCompleted ? "[x]" : "[ ]";
            var priorityIndicator = item.Priority switch
            {
                Priority.High => "!!!",
                Priority.Medium => "!! ",
                Priority.Low => "!  ",
                _ => "   "
            };
            Console.WriteLine($"{checkbox} #{item.Id,-3} {priorityIndicator} {item.Title}");
        }
        Console.WriteLine();
        break;

    case "complete":
        if (args.Length < 2 || !int.TryParse(args[1], out var completeId))
        {
            Console.WriteLine("Error: Please provide a valid task ID");
            Console.WriteLine("Usage: dotnet run complete <id>");
            return;
        }

        if (store.Complete(completeId))
        {
            Console.WriteLine($"Task #{completeId} marked as completed");
        }
        else
        {
            Console.WriteLine($"Error: Task #{completeId} not found");
        }
        break;

    case "delete":
        if (args.Length < 2 || !int.TryParse(args[1], out var deleteId))
        {
            Console.WriteLine("Error: Please provide a valid task ID");
            Console.WriteLine("Usage: dotnet run delete <id>");
            return;
        }

        if (store.Delete(deleteId))
        {
            Console.WriteLine($"Task #{deleteId} deleted");
        }
        else
        {
            Console.WriteLine($"Error: Task #{deleteId} not found");
        }
        break;

    case "help":
    case "--help":
    case "-h":
        ShowHelp();
        break;

    default:
        Console.WriteLine($"Unknown command: {command}");
        ShowHelp();
        break;
}

void ShowHelp()
{
    Console.WriteLine("Todo App - Command Line Task Manager");
    Console.WriteLine("\nUsage:");
    Console.WriteLine("  dotnet run add <title> [--priority high|medium|low]");
    Console.WriteLine("  dotnet run list [all|pending|completed]");
    Console.WriteLine("  dotnet run complete <id>");
    Console.WriteLine("  dotnet run delete <id>");
    Console.WriteLine("  dotnet run help");
    Console.WriteLine("\nExamples:");
    Console.WriteLine("  dotnet run add \"Buy groceries\" --priority high");
    Console.WriteLine("  dotnet run list pending");
    Console.WriteLine("  dotnet run complete 1");
    Console.WriteLine("  dotnet run delete 2");
}