
using System;
using System.Collections.Generic;
using System.Diagnostics;
using RecursionCodeGen;

namespace ExampleApp
{
    static class SimpleDoubleRecursion
    {
        static int F0(int n)
        {
            return n;
        }


        static int F1Rec(int n)
        {
            if (n == 0)
                return 0;
            return F1Rec(F1Rec(n - 1)) + 1;
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

        public static void TestRec()
        {
            Console.WriteLine("================================================================================================");
            Impl[] impls = new Impl[]
            {
                new Impl("F1Rec", F1Rec),
                new Impl("F2NoRec", F2NoRec)
            };

            const bool logGood = true;
            for (int i = 0; i < 25; i++)
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
                    // Console.WriteLine($"Testing {i} => calls {dbgCnt}");
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
    }
}
