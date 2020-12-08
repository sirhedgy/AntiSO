using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace AntiSO.CodeGen.Recursion
{
    internal class TargetMethodsInfo
    {
        private readonly Dictionary<string, List<RecursiveMethodInfo>> _storage = new Dictionary<string, List<RecursiveMethodInfo>>();

        public TargetMethodsInfo(IReadOnlyList<RecursiveMethodInfo> recursiveMethodInfos)
        {
            foreach (var methodInfo in recursiveMethodInfos)
            {
                Add(methodInfo);
            }
        }

        internal void Add(RecursiveMethodInfo value)
        {
            var key = value.MethodSymbol.Name;
            var list = GetListImpl(key, true);
            list.Add(value);
        }


        public RecursiveMethodInfo? this[ISymbol methodSymbol]
        {
            get
            {
                string key = methodSymbol.Name;
                var list = GetListImpl(key, false);
                return list.FirstOrDefault(mi => Cmp(mi.MethodSymbol, methodSymbol));
            }
        }

        private static bool Cmp(ISymbol s1, ISymbol s2)
        {
            return s1.Equals(s2, SymbolEqualityComparer.Default);
        }

        private List<RecursiveMethodInfo> GetListImpl(string key, bool addIfMissing = false)
        {
            if (!_storage.TryGetValue(key, out var list))
            {
                list = new List<RecursiveMethodInfo>();
                if (addIfMissing)
                    _storage[key] = list;
            }

            return list;
        }
    }
}