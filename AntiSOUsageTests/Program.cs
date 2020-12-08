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
            SimpleDoubleRecursion.TestDoubleRec();
            BinTreeRecTest.TestMax();
            MutualRecursion.TestIsOdd();

            Console.WriteLine();
            Console.WriteLine("Everything is good");
        }
    }
}
