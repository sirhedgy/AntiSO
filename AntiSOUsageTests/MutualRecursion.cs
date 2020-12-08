using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using AntiSO;
// using AntiSO.Infrastructure;

namespace AntiSOUsageTests
{
    public partial class MutualRecursion
    {
        private static bool IsOdd0(int n)
        {
            return n % 2 != 0;
        }

        private const string OddEvenMutualRecursionId = "IsOddEven";


        [GenerateSafeRecursion(MutualRecursionId = OddEvenMutualRecursionId, ExposeAsEntryPoint = true)]
        public static bool IsOddRec(int n)
        {
            if (n < 0)
                return IsOddRec(-n);
            var isEven = IsEvenRec(n);
            return !isEven;
        }

        [GenerateSafeRecursion(MutualRecursionId = OddEvenMutualRecursionId, ExposeAsEntryPoint = true)]
        public static bool IsEvenRec(int n)
        {
            if (n == 0)
                return true;
            return IsOddRec(n - 1);
        }


        class Manual
        {
            enum Method : int
            {
                IsOddRec,
                IsEvenRec
            }

            struct IsOddRecParams
            {
                internal readonly int n;

                public IsOddRecParams(int n)
                {
                    this.n = n;
                }
            }

            struct IsEvenRecParams
            {
                internal readonly int n;

                public IsEvenRecParams(int n)
                {
                    this.n = n;
                }
            }

            [StructLayout(LayoutKind.Explicit)]
            struct CallParams
            {
                [FieldOffset(0)]
                internal readonly Method Method;

                [FieldOffset(sizeof(int))]
                internal readonly IsOddRecParams IsOddRecParams;

                [FieldOffset(4)]
                internal readonly IsEvenRecParams IsEvenRecParams;

                internal CallParams(IsOddRecParams isOddRecParams) : this()
                {
                    Method = Method.IsOddRec;
                    IsOddRecParams = isOddRecParams;
                }

                internal CallParams(IsEvenRecParams isEvenRecParams) : this()
                {
                    Method = Method.IsEvenRec;
                    IsEvenRecParams = isEvenRecParams;
                }

                public override string ToString()
                {
                    if (Method == Method.IsEvenRec)
                    {
                        return $"{Method} {nameof(IsEvenRecParams)}: {IsEvenRecParams.n}";
                    }
                    else
                    {
                        return $"{Method}, {nameof(IsOddRecParams)}: {IsOddRecParams.n}";
                    }
                }
            }

            internal static bool IsOdd(int n)
            {
                var runner = new IsOddRecursionRunner();
                runner.RunRecursion(new CallParams(new IsOddRecParams(n)));
                return runner._lastReturnValue;
            }

            internal static bool IsEven(int n)
            {
                var runner = new IsOddRecursionRunner();
                runner.RunRecursion(new CallParams(new IsEvenRecParams(n)));
                return runner._lastReturnValue;
            }

            private class IsOddRecursionRunner : AntiSO.Infrastructure.SimpleRecursionRunner<CallParams>
            {
                internal bool _lastReturnValue;

                protected override IEnumerator<CallParams> ComputeImpl(CallParams callParams)
                {
                    switch (callParams.Method)
                    {
                        case Method.IsEvenRec:
                            return IsEvenRec(callParams.IsEvenRecParams.n);
                        case Method.IsOddRec:
                            return IsOddRec(callParams.IsOddRecParams.n);
                        default:
                            throw new InvalidOperationException($"Unexpected method call {callParams.Method}");
                    }
                }

                private IEnumerator<CallParams> IsOddRec(int n)
                {
                    if (n < 0)
                    {
                        //return !IsEvenRec(-n);
                        yield return new CallParams(new IsOddRecParams(-n));
                        yield break;
                    }

                    //return !IsEvenRec(n);
                    yield return new CallParams(new IsEvenRecParams(n));
                    _lastReturnValue = !_lastReturnValue;
                }

                private IEnumerator<CallParams> IsEvenRec(int n)
                {
                    if (n == 0)
                    {
                        _lastReturnValue = true;
                        yield break;
                    }

                    //return !IsOddRec(n-1);
                    yield return new CallParams(new IsOddRecParams(n - 1));
                }
            }
        }


        struct IsOddImpl
        {
            internal readonly string Name;
            internal readonly Func<int, bool> Fn;

            public IsOddImpl(string name, Func<int, bool> fn)
            {
                this.Name = name;
                this.Fn = fn;
            }
        }


        public static void TestIsOdd()
        {
            Console.WriteLine("================================================================================================");
            IsOddImpl[] impls = new IsOddImpl[]
            {
                new IsOddImpl("IsOddRec", IsOddRec),
                new IsOddImpl("IsOddManual", Manual.IsOdd),
                new IsOddImpl("IsOddRec_GenSafeRec", IsOddRec_GenSafeRec),
            };

            const bool logGood = true;
            for (int i = -40; i <= 40; i += 5)
            {
                if (logGood)
                    Console.WriteLine($"Testing IsOdd {i}");
                var r0 = IsOdd0(i);
                var sw = new Stopwatch();
                foreach (var impl in impls)
                {
                    sw.Reset();
                    sw.Start();
                    var resImpl = impl.Fn(i);
                    sw.Stop();
                    if (r0 != resImpl)
                    {
                        Console.WriteLine($"for {i} '{impl.Name}', r0 = {r0} != resImpl = {resImpl}");
                        throw new Exception($"for {i} '{impl.Name}', r0 = {r0} != resImpl = {resImpl}");
                    }
                    else if (logGood)
                    {
                        Console.WriteLine($"for {i} '{impl.Name}' took {sw.Elapsed} to calculate result");
                    }
                }
            }
        }
    }
}