using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AntiSO.CodeGen
{
    internal static class SyntaxUtils
    {
        internal static string ToFullStringTrimmed(this SyntaxNode node)
        {
            return node.ToFullString().Trim();
        }

        internal static BlockSyntax GetBlockFromCodeString(SyntaxNode origNode, string code, bool removeBraces = false)
        {
            var origCode = origNode.ToFullStringTrimmed().Replace("\n", "\n//");
            var wrappedCode =
                $@"          
                    {{
                    // Replace '{origCode}'
                    {code}
                    // End replace '{origCode}' {((removeBraces) ? "\n\t\t\t;" : "") /* if removeBraces is true, add an empty ';' for the end comment to survive when we remove the braces*/ }
                    }}
                    ";

            var tree = CSharpSyntaxTree.ParseText(wrappedCode);
            var root = (CompilationUnitSyntax) tree.GetRoot();
            var global = (GlobalStatementSyntax) root.Members[0];
            var block = (BlockSyntax) global.Statement;
            if (!removeBraces)
            {
                return block;
            }
            else
            {
                // This MissingToken looks like a total hack but seems to work.
                // The idea of this hack is that for variable declaration we can't introduce 
                // a real wrapping block because of the visibility but we still want to return
                // a single statement so we produce a fake block with no braces ("{"","}")
                // It seems to work OK at least when we convert this block to string as
                // a new compilation unit (file). 
                return block.Update(block.AttributeLists,
                    SyntaxFactory.MissingToken(SyntaxKind.OpenBraceToken),
                    block.Statements,
                    SyntaxFactory.MissingToken(SyntaxKind.CloseBraceToken));
            }
        }


        private static readonly SymbolDisplayFormat FullTypeFormat =
            new SymbolDisplayFormat(typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces);

        internal static bool MatchesRuntimeType(this ITypeSymbol typeSymbol, Type type)
        {
            return type.FullName == (typeSymbol.ToDisplayString(FullTypeFormat));
        }
    }
}