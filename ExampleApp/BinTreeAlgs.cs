using System;
using System.Collections.Generic;
using RecursionCodeGen;

namespace ExampleApp
{
    public class BinTree<T>
    {
        public T Value { get; set; }
        public BinTree<T> LeftChild { get; set; }
        public BinTree<T> RightChild { get; set; }

        public BinTree(T value)
        {
            Value = value;
        }
    }


    public static class BinTreeUtils
    {
        public static T FinMaxRec<T>(this BinTree<T> tree) where T : IComparable<T>
        {
            var max = tree.Value;
            if (tree.LeftChild != null)
            {
                var lMax = FinMaxRec(tree.LeftChild);
                if (lMax.CompareTo(max) > 0)
                    max = lMax;
            }

            if (tree.RightChild != null)
            {
                var rMax = FinMaxRec(tree.RightChild);
                if (rMax.CompareTo(max) > 0)
                    max = rMax;
            }

            return max;
        }

        public static T FinMaxNoRec<T>(this BinTree<T> tree) where T : IComparable<T>
        {
            return new FindMaxCalculator<T>().Calculate(tree);
        }

        internal struct FindMaxCallParams<T>
        {
            internal readonly BinTree<T> tree;

            public FindMaxCallParams(BinTree<T> tree)
            {
                this.tree = tree;
            }
        }

        internal class FindMaxCalculator<T> : SimpleRecursionRunner<FindMaxCallParams<T>, T>
            where T : IComparable<T>
        {
            protected override IEnumerator<FindMaxCallParams<T>> ComputeImpl(FindMaxCallParams<T> callParams)
            {
                var max = callParams.tree.Value;
                if (callParams.tree.LeftChild != null)
                {
                    yield return new FindMaxCallParams<T>(callParams.tree.LeftChild);
                    var lMax = _lastReturnValue;
                    if (lMax.CompareTo(max) > 0)
                        max = lMax;
                }

                if (callParams.tree.RightChild != null)
                {
                    yield return new FindMaxCallParams<T>(callParams.tree.RightChild);
                    var rMax = _lastReturnValue;
                    if (rMax.CompareTo(max) > 0)
                        max = rMax;
                }

                _lastReturnValue = max;
            }

            public T Calculate(BinTree<T> tree)
            {
                return RunRecursion(new FindMaxCallParams<T>(tree));
            }
        }
    }

    static class BinTreeRecTest
    {
        private static BinTree<int> BuildTreeImpl(Random rnd, int maxVal, int cnt)
        {
            if (cnt == 0)
                return null;
            var root = new BinTree<int>(rnd.Next(maxVal));
            var leftCnt = rnd.Next(cnt - 1);
            var rightCnt = cnt - leftCnt - 1;
            root.LeftChild = BuildTreeImpl(rnd, maxVal, leftCnt);
            root.RightChild = BuildTreeImpl(rnd, maxVal, rightCnt);
            return root;
        }

        private static BinTree<int> BuildTree0()
        {
            var rnd = new Random(0);
            return BuildTreeImpl(rnd, 1024, 3);
        }

        private static BinTree<int> BuildTree1()
        {
            var rnd = new Random(0);
            return BuildTreeImpl(rnd, 1024, 100);
        }

        public static void TestMax()
        {
            Console.WriteLine("================================================================================================");
            var tree = BuildTree1();
            var res1 = tree.FinMaxRec();
            var res2 = tree.FinMaxNoRec();
            if (res1 != res2)
            {
                Console.WriteLine($"res1 = {res1} != res2 = {res2}");
            }
            else
            {
                Console.WriteLine("FinMaxNoRec == FinMaxNoRec");
            }
        }
    }
}