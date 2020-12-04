using System;
using AntiSOUsageTests;

namespace AntiSOUsageTests
{
    class Program
    {
        static void Main(string[] args)
        {
            SimpleRecursion.TestGcd();
            SimpleRecursion.TestFib();
            Console.WriteLine("Hello World!");
            SimpleDoubleRecursion.TestDoubleRec();
            BinTreeRecTest.TestMax();
        }
    }
}
