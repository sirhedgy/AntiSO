using System;
using System.Collections.Generic;
using System.Diagnostics;
using AntiSO;
using AntiSO.Infrastructure;

namespace AntiSOUsageTests
{
    static partial class SimpleDoubleRecursion
    {
        static int F0(int n)
        {
            return n;
        }


        static int F1Rec(int n)
        {
            if (n == 0)
                return 0;
            // this kind of code is not supported yet
            return F1Rec(F1Rec(n - 1)) + 1;
        }

        [GenerateSafeRecursion]
        static int F3Rec(int n)
        {
            if (n == 0)
                return 0;
            var res = F3Rec(n - 1);
            res = F3Rec(res);
            return res + 1;
        }


        static int F2NoRec(int n)
        {
            return new F2Calculator().Calculate(n);
        }

        internal struct F2CallParams
        {
            internal readonly int n;

            internal F2CallParams(int n)
            {
                this.n = n;
            }
        }

        internal sealed class F2Calculator : SimpleRecursionRunner<F2CallParams, int>
        {
            internal int Calculate(int n)
            {
                return RunRecursion(new F2CallParams(n));
            }

            protected override IEnumerator<F2CallParams> ComputeImpl(F2CallParams callParams)
            {
                if (callParams.n == 0)
                {
                    _lastReturnValue = 0;
                    yield break;
                }

                yield return new F2CallParams(callParams.n - 1);
                int innerRes = _lastReturnValue;
                yield return new F2CallParams(innerRes);
                int outerRes = _lastReturnValue;
                _lastReturnValue = outerRes + 1;
            }
        }


        struct Impl
        {
            public readonly string name;
            public readonly Func<int, int> fn;

            public Impl(string name, Func<int, int> fn)
            {
                this.name = name;
                this.fn = fn;
            }
        }

        public static void TestDoubleRec()
        {
            Console.WriteLine("================================================================================================");
            Impl[] impls = new Impl[]
            {
                new Impl("F1Rec", F1Rec),
                new Impl("F2NoRec", F2NoRec),
                new Impl("F3Rec_GenSafeRec", F3Rec_GenSafeRec)
            };

            const bool logGood = true;
            for (int i = 0; i <= 20; i++)
            {
                if (logGood)
                    Console.WriteLine($"Testing {i}");
                var r0 = F0(i);
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
                        throw new Exception($"for {i} '{impl.name}', r0 = {r0} != resImpl = {resImpl}");
                    }
                    else if (logGood)
                    {
                        Console.WriteLine($"for {i} '{impl.name}' took {sw.Elapsed} to calculate result");
                    }
                }
            }
        }
    }
}