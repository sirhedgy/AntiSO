using System;
using AntiSO;

namespace Sample.Lib
{
    public static partial class LibClassWithRecursion
    {
        /// <summary>
        /// Just a prototype that will never be used in practice. The generated "Gcd" method
        /// will always be used instead. Compare this to <see cref="Fibonacci"/> case below.
        /// </summary>
        [GenerateSafeRecursion(AccessLevel = AccessLevel.Public, GeneratedMethodName = "Gcd")]
        private static int GcdRecPrototype(int a, int b)
        {
            if (a == 0)
                return b;
            if (b == 0)
                return a;
            return GcdRecPrototype(b, a % b);
        }

        #region Fibonacci

        /// <summary>
        /// A proxy method that uses a size heuristic to either use the original or the generated version.
        /// </summary>
        public static long Fibonacci(int n)
        {
            if (n < 20)
            {
                Console.WriteLine($"Use the original Fibonacci code for {n}");
                return FibRecImpl(n);
            }
            else
            {
                Console.WriteLine($"Use the generated Fibonacci code for {n}");
                return FibRecImpl_GenSafeRec(n);
            }
        }

        [GenerateSafeRecursion()]
        private static long FibRecImpl(int n)
        {
            if (n <= 1)
                return 1;
            var f1 = FibRecImpl(n - 1);
            var f2 = FibRecImpl(n - 2);
            return f1 + f2;
        }

        #endregion
    }
}