using System;

namespace CalcFromConfig
{
    class Program
    {
        static int Main(string[] args)
        {
            if (args.Length == 0 || args[0] == "--help" || args[0] == "-h")
            {
                ShowHelp();
                return 0;
            }

            if (args.Length != 3)
            {
                Console.Error.WriteLine("Error: Invalid number of arguments.");
                Console.Error.WriteLine("Usage: calc <operation> <number1> <number2>");
                Console.Error.WriteLine("Run 'calc --help' for more information.");
                return 1;
            }

            string operation = args[0].ToLower();

            if (!double.TryParse(args[1], out double num1))
            {
                Console.Error.WriteLine($"Error: '{args[1]}' is not a valid number.");
                return 1;
            }

            if (!double.TryParse(args[2], out double num2))
            {
                Console.Error.WriteLine($"Error: '{args[2]}' is not a valid number.");
                return 1;
            }

            double result;

            switch (operation)
            {
                case "add":
                    result = num1 + num2;
                    break;
                case "subtract":
                    result = num1 - num2;
                    break;
                case "multiply":
                    result = num1 * num2;
                    break;
                case "divide":
                    if (num2 == 0)
                    {
                        Console.Error.WriteLine("Error: Division by zero is not allowed.");
                        return 1;
                    }
                    result = num1 / num2;
                    break;
                default:
                    Console.Error.WriteLine($"Error: Unknown operation '{operation}'.");
                    Console.Error.WriteLine("Supported operations: add, subtract, multiply, divide");
                    return 1;
            }

            Console.WriteLine(result);
            return 0;
        }

        static void ShowHelp()
        {
            Console.WriteLine("Calculator - Command Line Tool");
            Console.WriteLine();
            Console.WriteLine("Usage: calc <operation> <number1> <number2>");
            Console.WriteLine();
            Console.WriteLine("Operations:");
            Console.WriteLine("  add        Add two numbers");
            Console.WriteLine("  subtract   Subtract second number from first");
            Console.WriteLine("  multiply   Multiply two numbers");
            Console.WriteLine("  divide     Divide first number by second");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  calc add 5 3        Output: 8");
            Console.WriteLine("  calc subtract 10 4  Output: 6");
            Console.WriteLine("  calc multiply 7 6   Output: 42");
            Console.WriteLine("  calc divide 15 3    Output: 5");
        }
    }
}
