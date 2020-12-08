using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using AntiSO.Infrastructure;

namespace AntiSOUsageTests
{
    public class MutualRecursion
    {
        private static bool IsOdd0(int n)
        {
            return n % 2 != 0;
        }

        public static bool IsOddRec(int n)
        {
            if (n < 0)
                return IsOddRec(-n);
            return !IsEvenRec(n);
        }

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

                [FieldOffset(4)]
                internal readonly IsOddRecParams IsOddRecParams;

                [FieldOffset(4)]
                internal readonly IsEvenRecParams IsEvenRecParams;

                internal CallParams(Method method, IsOddRecParams isOddRecParams) : this()
                {
                    Method = method;
                    IsOddRecParams = isOddRecParams;
                }

                internal CallParams(Method method, IsEvenRecParams isEvenRecParams) : this()
                {
                    Method = method;
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
                return new IsOddRecursionRunner().RunRecursion(new CallParams(Method.IsOddRec, new IsOddRecParams(n)));
            }

            internal static bool IsEven(int n)
            {
                return new IsOddRecursionRunner().RunRecursion(new CallParams(Method.IsEvenRec, new IsEvenRecParams(n)));
            }

            private class IsOddRecursionRunner : SimpleRecursionRunner<CallParams,bool >
            {
                protected override IEnumerator<CallParams> ComputeImpl(CallParams callParams)
                {
                    switch (callParams.Method)
                    {
                        case Method.IsEvenRec:
                            return IsEvenRec(callParams.IsEvenRecParams.n);
                        case Method.IsOddRec:
                            return IsOddRec(callParams.IsOddRecParams.n);
                        default:
                            throw new InvalidOperationException($"Unexpected method call ${callParams.Method}");
                    }
                }

                private IEnumerator<CallParams> IsOddRec(int n)
                {
                    if (n < 0)
                    {
                        yield return new CallParams(Method.IsOddRec, new IsOddRecParams(-n));
                        yield break;
                    }

                    yield return new CallParams(Method.IsEvenRec, new IsEvenRecParams(n));
                    _lastReturnValue = !_lastReturnValue;
                }

                private IEnumerator<CallParams> IsEvenRec(int n)
                {
                    if (n == 0)
                    {
                        _lastReturnValue = true;
                        yield break;
                    }

                    yield return new CallParams(Method.IsOddRec, new IsOddRecParams(n - 1));
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
                new IsOddImpl("IsOddManual", Manual.IsOdd)
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