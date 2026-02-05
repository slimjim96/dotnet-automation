using System;
using System.Linq;

namespace SimpleApp
{
    class Program
    {
        static int Main(string[] args)
        {
            if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
            {
                ShowHelp();
                return 0;
            }

            if (args.Length != 3)
            {
                Console.Error.WriteLine("Error: Invalid number of arguments.");
                Console.Error.WriteLine("Use --help for usage information.");
                return 1;
            }

            string operation = args[0].ToLower();

            if (!double.TryParse(args[1], out double num1))
            {
                Console.Error.WriteLine($"Error: Invalid first number '{args[1]}'");
                return 1;
            }

            if (!double.TryParse(args[2], out double num2))
            {
                Console.Error.WriteLine($"Error: Invalid second number '{args[2]}'");
                return 1;
            }

            double result;
            try
            {
                result = operation switch
                {
                    "add" => Add(num1, num2),
                    "subtract" => Subtract(num1, num2),
                    "multiply" => Multiply(num1, num2),
                    "divide" => Divide(num1, num2),
                    _ => throw new ArgumentException($"Unknown operation: {operation}")
                };

                Console.WriteLine(result);
                return 0;
            }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                Console.Error.WriteLine("Valid operations: add, subtract, multiply, divide");
                return 1;
            }
            catch (DivideByZeroException)
            {
                Console.Error.WriteLine("Error: Cannot divide by zero");
                return 1;
            }
        }

        static double Add(double a, double b) => a + b;

        static double Subtract(double a, double b) => a - b;

        static double Multiply(double a, double b) => a * b;

        static double Divide(double a, double b)
        {
            if (b == 0)
                throw new DivideByZeroException();
            return a / b;
        }

        static void ShowHelp()
        {
            Console.WriteLine("Simple Calculator CLI");
            Console.WriteLine();
            Console.WriteLine("Usage: SimpleApp <operation> <number1> <number2>");
            Console.WriteLine();
            Console.WriteLine("Operations:");
            Console.WriteLine("  add        Add two numbers");
            Console.WriteLine("  subtract   Subtract second number from first");
            Console.WriteLine("  multiply   Multiply two numbers");
            Console.WriteLine("  divide     Divide first number by second");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  SimpleApp add 5 3");
            Console.WriteLine("  SimpleApp subtract 10 4");
            Console.WriteLine("  SimpleApp multiply 6 7");
            Console.WriteLine("  SimpleApp divide 15 3");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  --help, -h Show this help message");
        }
    }
}
