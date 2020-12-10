using System;
using System.Collections.Generic;
using AntiSO;
using AntiSO.Infrastructure;

namespace AntiSOUsageTests
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


    public static partial class BinTreeUtils
    {
        #region FindMax

        [GenerateSafeRecursion]
        public static T FindMaxRec<T>(this BinTree<T> tree) where T : IComparable<T>
        {
            var max = tree.Value;

            if ((tree.LeftChild != null) && (tree.RightChild != null))
            {
                // multiple declarations with assignments
                T tmp1 = default(T),
                    lMax = FindMaxRec(tree.LeftChild),
                    tmp2 = lMax,
                    rMax = FindMaxRec(tree.RightChild);

                if (lMax.CompareTo(max) > 0)
                    max = lMax;
                if (rMax.CompareTo(max) > 0)
                    max = rMax;
            }
            else
            {
                if (tree.LeftChild != null)
                {
                    var lMax = FindMaxRec(tree.LeftChild);
                    if (lMax.CompareTo(max) > 0)
                        max = lMax;
                }

                if (tree.RightChild != null)
                {
                    // var rMax = FindMaxRec(tree.RightChild);
                    T rMax;
                    rMax = FindMaxRec(tree.RightChild);
                    if (rMax.CompareTo(max) > 0)
                        max = rMax;
                }
            }

            return max;
        }

        public static T FindMaxNoRec<T>(this BinTree<T> tree) where T : IComparable<T>
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

        internal class FindMaxCalculator<T> : SimpleRecursionRunner<FindMaxCallParams<T>>
            where T : IComparable<T>
        {
            private T _lastReturnValue;

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
                RunRecursion(new FindMaxCallParams<T>(tree));
                return _lastReturnValue;
            }
        }

        #endregion


        #region Dfs

        [GenerateSafeRecursion(GeneratedMethodName = "DfsNoRec")]
        public static void DfsRec<T>(BinTree<T> tree, Action<T> action) where T : IComparable<T>
        {
            if (tree.LeftChild != null)
                DfsRec<T>(tree.LeftChild, action);
            if (tree.RightChild != null)
                DfsRec(tree.RightChild, action);
            action(tree.Value);
        }

        [GenerateSafeRecursion(GeneratedMethodName = "DfsMutNoRec", MutualRecursionId = "DfsMutNoRec")]
        public static void DfsMutRec<T>(BinTree<T> tree, Action<T> action) where T : IComparable<T>
        {
            if (tree.LeftChild != null)
                DfsMutLeftRec<T>(tree.LeftChild, action);
            if (tree.RightChild != null)
                DfsRec(tree.RightChild, action);
            action(tree.Value);
        }

        [GenerateSafeRecursion(MutualRecursionId = "DfsMutNoRec", ExposeAsEntryPoint = false)]
        public static void DfsMutLeftRec<T>(BinTree<T> tree, Action<T> action) where T : IComparable<T>
        {
            if (tree.LeftChild != null)
                // DfsMutLeftRec<T>(tree.LeftChild, action);
                DfsMutLeftRec(tree.LeftChild, action);
            if (tree.RightChild != null)
                // DfsMutRec(tree.RightChild, action);
                DfsMutRec<T>(tree.RightChild, action);
            action(tree.Value);
        }

        #endregion
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

        public static void TestFindMax()
        {
            Console.WriteLine("================================================================================================");
            var tree = BuildTree1();
            var res1 = tree.FindMaxRec();
            // var res2 = tree.FidnMaxNoRec();
            var res2 = tree.FindMaxRec_GenSafeRec();
            if (res1 != res2)
            {
                Console.WriteLine($"FindMax res1 = {res1} != res2 = {res2}");
                throw new Exception($"FindMax res1 = {res1} != res2 = {res2}");
            }
            else
            {
                Console.WriteLine("FindMaxRec == FinMaxNoRec");
            }
        }

        #region Dfs

        private delegate void DfsDelegate(BinTree<int> tree, Action<int> action);

        private static long SumUsingDfs(BinTree<int> tree, DfsDelegate dfs)
        {
            long sum = 0;
            dfs(tree, value => sum += value);
            return sum;
        }

        public static void TestDfs()
        {
            Console.WriteLine("================================================================================================");
            var tree = BuildTree1();
            var res1 = SumUsingDfs(tree, BinTreeUtils.DfsRec);

            var res2 = SumUsingDfs(tree, BinTreeUtils.DfsNoRec);
            if (res1 != res2)
            {
                Console.WriteLine($"Dfs res1 = {res1} != res2 = {res2}");
                throw new Exception($"res1 = {res1} != res2 = {res2}");
            }
            else
            {
                Console.WriteLine("DfsRec == DfsNoRec");
            }

            var res3 = SumUsingDfs(tree, BinTreeUtils.DfsMutNoRec);
            if (res1 != res3)
            {
                Console.WriteLine($"Dfs res1 = {res1} != res3 = {res3} Mut");
                throw new Exception($"res1 = {res1} != res3 = {res3}");
            }
            else
            {
                Console.WriteLine("DfsRec == DfsMutNoRec");
            }
        }

        #endregion
    }
}