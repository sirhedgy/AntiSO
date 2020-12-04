using System;
using Microsoft.CodeAnalysis;

namespace AntiSO.CodeGen
{
    internal static class SyntaxUtils
    {
        internal static string ToFullStringTrimmed(this SyntaxNode node)
        {
            return node.ToFullString().Trim();
        }

        private static readonly SymbolDisplayFormat FullTypeFormat =
            new SymbolDisplayFormat(typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces);

        internal static bool MatchesRuntimeType(this ITypeSymbol typeSymbol, Type type)
        {
            return type.FullName == (typeSymbol.ToDisplayString(FullTypeFormat));
        }
    }
}