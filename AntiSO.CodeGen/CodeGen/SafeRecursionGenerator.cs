using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AntiSO.CodeGen.Recursion;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace AntiSO.CodeGen
{
    [Generator]
    // ReSharper disable once ClassNeverInstantiated.Global
    public class SafeRecursionGenerator : ISourceGenerator
    {
        internal static readonly Type TargetAttribute = typeof(GenerateSafeRecursionAttribute);
        private static readonly string TargetAttributeShortTypeName = TargetAttribute.Name;
        private static readonly string TargetAttributeShortName = TargetAttributeShortTypeName.Substring(0, TargetAttributeShortTypeName.Length - "Attribute".Length);


        public void Initialize(GeneratorInitializationContext context)
        {
            // Register a syntax receiver that will be created for each generation pass
            context.RegisterForSyntaxNotifications(() => new RecursionSyntaxReceiver());
        }

        public void Execute(GeneratorExecutionContext context)
        {
            // retrieve the populated receiver 
            if (!(context.SyntaxReceiver is RecursionSyntaxReceiver receiver))
                return;

            var compilation = context.Compilation;
            if ("C#" != compilation.Language)
            {
                context.ReportUnsupportedLanguageError(compilation.Language);
                return;
            }

            var goodCandidates = receiver.CandidateMethods.Select(methodSyntax =>
                {
                    var methodModel = compilation.GetSemanticModel(methodSyntax.SyntaxTree);
                    var methodSymbol = methodModel.GetDeclaredSymbol(methodSyntax);
                    var attr = methodSymbol.GetAttributes().SingleOrDefault(a => a.AttributeClass.MatchesRuntimeType(TargetAttribute));
                    var codeGenProps = (attr != null) ? SafeRecursionCodeGenProps.ParseFromAttribute(context, methodSyntax, attr) : null;
                    return new RecursiveMethodInfo(methodSyntax, methodSymbol, methodModel, codeGenProps);
                })
                .Where(t => t.CodeGenProps != null)
                .GroupBy(t => t.CodeGenProps.MutualRecursionId ?? "") //convert null to empty string for GroupBy 
                .ToDictionary(g => g.Key, g => g.ToList());

            if (!goodCandidates.TryGetValue("", out var simpleCandidates))
            {
                simpleCandidates = new List<RecursiveMethodInfo>();
            }

            context.Log($"Found {simpleCandidates.Count} method(s) for simple recursion and {goodCandidates.Count - 1} method group(s) for mutual recursion generation.");

            foreach (var methodInfo in simpleCandidates)
            {
                try
                {
                    var generatedCode = CalculatorClassGenerator.GenerateSimpleSafeRec(context, methodInfo);
                    if (generatedCode != null)
                    {
                        context.AddSource($"{methodInfo.MethodSymbol.ContainingType.Name}_{methodInfo.MethodSymbol.Name}_SafeRecursion.cs",
                            SourceText.From(generatedCode, Encoding.UTF8));
                    }
                }
                catch (Exception e)
                {
                    context.LogInternalError(methodInfo.MethodSyntax.GetLocation(),
                        $"Processing '{methodInfo.MethodSymbol.ContainingType.Name}' resulted in an internal error '{e}'");
                }
            }

            foreach (var (recursionId, methodInfos) in goodCandidates)
            {
                // skip the simple candidates
                if (string.IsNullOrEmpty(recursionId))
                    continue;
                var someMethodInfo = methodInfos.First();
                try
                {
                    var generatedCode = CalculatorClassGenerator.GenerateMutualSafeRec(context, methodInfos);
                    if (generatedCode != null)
                    {
                        context.AddSource($"{someMethodInfo.MethodSymbol.ContainingType.Name}_{recursionId}_SafeRecursion.cs",
                            SourceText.From(generatedCode, Encoding.UTF8));
                    }
                }
                catch (Exception e)
                {
                    context.LogInternalError(someMethodInfo.MethodSyntax.Parent.GetLocation(),
                        $"Processing '{recursionId}' mutual recursion resulted in an internal error '{e}'");
                }
            }
        }

        private class RecursionSyntaxReceiver : ISyntaxReceiver
        {
            public List<MethodDeclarationSyntax> CandidateMethods { get; } = new List<MethodDeclarationSyntax>();

            public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
            {
                // any field with at least one attribute is a candidate for property generation
                if (syntaxNode is MethodDeclarationSyntax methodDeclarationSyntax
                    // fast and dirty check for our marker attribute
                    && methodDeclarationSyntax.AttributeLists.Any(al =>
                        al.Attributes.Any(a => a.Name.ToString().Contains(TargetAttributeShortName))))
                {
                    CandidateMethods.Add(methodDeclarationSyntax);
                }
            }
        }
    }
}