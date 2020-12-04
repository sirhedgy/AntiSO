using System;
using Sample.Lib;

namespace Sample.App
{
    class Program
    {
        static void Main(string[] args)
        {
            var g = LibClassWithRecursion.Gcd(42, 42024);
            Console.WriteLine($"Gcd = {g}");
            
            // this will be calculated using the original code
            var f10 = LibClassWithRecursion.Fibonacci(10);
            Console.WriteLine($"f10 = {f10}");
            // this will be calculated using the generated code
            var f30 = LibClassWithRecursion.Fibonacci(30);
            Console.WriteLine($"f35 = {f30}");
        }
    }
}