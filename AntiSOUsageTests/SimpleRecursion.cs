using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using AntiSO;

namespace AntiSOUsageTests
{
    public static partial class SimpleRecursion
    {
        #region GCD

        [GcdCustom(1, Prop2 = 2)]
        [GenerateSafeRecursion]
        static int Gcd(int a, int b)
        {
            if (a == 0)
                return b;
            if (b == 0)
                return a;
            return Gcd(b, a % b);
        }

        class GcdCustomAttribute : Attribute
        {
            public int Prop1 { get; }
            public int Prop2 { get; set; }

            public GcdCustomAttribute(int prop1)
            {
                Prop1 = prop1;
            }
        }


        struct GcdImpl
        {
            public readonly string name;
            public readonly Func<int, int, int> fn;

            public GcdImpl(string name, Func<int, int, int> fn)
            {
                this.name = name;
                this.fn = fn;
            }
        }

        private static void CheckGcdAttributes()
        {
            var methodOrig = typeof(SimpleRecursion).GetMethod("Gcd", BindingFlags.Static | BindingFlags.NonPublic);
            var methodGen = typeof(SimpleRecursion).GetMethod("Gcd_GenSafeRec", BindingFlags.Static | BindingFlags.NonPublic);
            var attrsOrig = methodOrig.GetCustomAttributes().ToHashSet();
            var attrsGen = methodGen.GetCustomAttributes().ToHashSet();
            foreach (var attr in attrsOrig)
            {
                if (attr is GenerateSafeRecursionAttribute)
                    continue;
                if (!attrsGen.Contains(attr))
                {
                    throw new Exception($"Missing attribute {attr} on the Gcd_GenSafeRec");
                }
            }

            if (Attribute.GetCustomAttribute(methodGen, typeof(GenerateSafeRecursionAttribute)) != null)
            {
                throw new Exception($"The attribute '{nameof(GenerateSafeRecursionAttribute)}' has been copied to Gcd_GenSafeRec");
            }

            var attrOrig = (GcdCustomAttribute) Attribute.GetCustomAttribute(methodOrig, typeof(GcdCustomAttribute));
            var attrGen = (GcdCustomAttribute) Attribute.GetCustomAttribute(methodGen, typeof(GcdCustomAttribute));
            if (attrOrig.Prop1 != attrGen.Prop1)
            {
                throw new Exception($"Wrong attribute property value on the Gcd_GenSafeRec '{nameof(GcdCustomAttribute.Prop1)}' '{attrOrig.Prop1}' != '{attrOrig.Prop1}' ");
            }

            if (attrOrig.Prop2 != attrGen.Prop2)
            {
                throw new Exception($"Wrong attribute property value on the Gcd_GenSafeRec '{nameof(GcdCustomAttribute.Prop2)}' '{attrOrig.Prop2}' != '{attrOrig.Prop2}' ");
            }
        }

        public static void TestGcd()
        {
            Console.WriteLine("================================================================================================");
            GcdImpl[] impls = new GcdImpl[]
            {
                new GcdImpl("Gcd", Gcd),
                new GcdImpl("Gcd_GenSafeRec", Gcd_GenSafeRec)
            };
            CheckGcdAttributes();

            const bool logGood = true;
            // const int maxRnd = 65_536;
            var rnd = new Random(0);
            for (int i = 0; i < 100; i++)
            {
                var c = rnd.Next(char.MaxValue);
                if (c > 100)
                {
                    c = 1;
                }

                var a = rnd.Next(int.MaxValue / c) * c;
                var b = rnd.Next(int.MaxValue / c) * c;
                // if (logGood)
                //     Console.WriteLine($"Testing {a},{b}");
                var r0 = Gcd(a, b);
                var sw = new Stopwatch();
                foreach (var impl in impls)
                {
                    sw.Reset();
                    sw.Start();
                    var resImpl = impl.fn(a, b);
                    sw.Stop();
                    if (r0 != resImpl)
                    {
                        Console.WriteLine($"for {a},{b} '{impl.name}', r0 = {r0} != resImpl = {resImpl}");
                        throw new Exception($"for {a},{b} '{impl.name}', r0 = {r0} != resImpl = {resImpl}");
                    }
                    else if (logGood)
                    {
                        Console.WriteLine($"for {a},{b} '{impl.name}' took {sw.Elapsed} to calculate result");
                    }
                }
            }
        }

        #endregion


        #region Fibonacci

        static long Fib0(int n)
        {
            if (n <= 1)
                return 1;
            // this is not supported by the generator yet
            return Fib1Rec(n - 1) + Fib1Rec(n - 2);
        }

        [GenerateSafeRecursion(AccessLevel = AccessLevel.Public, GeneratedMethodName = "Fib1", ExtensionMethod = ExtensionMethod.Extenstion)]
        private static long Fib1Rec(int n)
        {
            if (n <= 1)
                return 1;
            var f1 = Fib1Rec(n - 1);
            var f2 = Fib1Rec(n - 2);
            return f1 + f2;
        }


        struct FibImpl
        {
            public readonly string name;
            public readonly Func<int, long> fn;

            public FibImpl(string name, Func<int, long> fn)
            {
                this.name = name;
                this.fn = fn;
            }
        }

        public static void TestFib()
        {
            Console.WriteLine("================================================================================================");
            FibImpl[] impls = new FibImpl[]
            {
                new FibImpl("Fib", Fib0),
                new FibImpl("Fib1_GenSafeRec", Fib1)
            };

            const bool logGood = true;
            for (int i = 0; i <= 30; i++)
            {
                if (logGood)
                    Console.WriteLine($"Testing {i}");
                var r0 = Fib0(i);
                var sw = new Stopwatch();
                foreach (var impl in impls)
                {
                    sw.Reset();
                    sw.Start();
                    var resImpl = impl.fn(i);
                    sw.Stop();
                    if (r0 != resImpl)
                    {
                        Console.WriteLine($"for {i} '{impl.name}', r0 = {r0} != resImpl = {resImpl}");
                    }
                    else if (logGood)
                    {
                        Console.WriteLine($"for {i} '{impl.name}' took {sw.Elapsed} to calculate result");
                    }
                }
            }
        }

        #endregion
    }
}