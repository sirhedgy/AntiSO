using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AntiSO.CodeGen.Recursion
{
    internal sealed class RecursiveCalculatorClassInfo
    {
        internal readonly ClassDeclarationSyntax ContainingClassSyntax;
        internal readonly INamedTypeSymbol ContainingClassSymbol;

        internal readonly string RecursionId;
        internal readonly bool IsMultiSite;
        internal readonly string GenericParams;
        internal readonly string ConstraintClauses;
        internal readonly string ParamsStructName;

        internal const string DispatchStructSuffix = "_RecDispatchCallParams";
        internal const string CallSiteEnumFieldName = "CallSite";
        internal string CallSiteEnumName => RecursionId + "_" + CallSiteEnumFieldName;

        internal string CalculatorClassName => RecursionId + "_RecRunner";

        public RecursiveCalculatorClassInfo(ClassDeclarationSyntax containingClassSyntax, INamedTypeSymbol containingClassSymbol, bool isMultiSite, string recursionId,
            string genericParams, string constraintClauses, string paramsStructName)
        {
            ContainingClassSyntax = containingClassSyntax;
            ContainingClassSymbol = containingClassSymbol;

            IsMultiSite = isMultiSite;
            RecursionId = recursionId;
            GenericParams = genericParams;
            ConstraintClauses = constraintClauses;
            ParamsStructName = paramsStructName;
        }
    }


    /// <summary>
    /// This is the class that generates the "calculator" class for the recursion transformation.
    /// It uses <see cref="CalculatorMethodGenerator"/> to generate the transformed methods.
    /// 
    /// <see cref="GenerateSimpleSafeRec"/> and <see cref="GenerateMutualSafeRec"/> are the entry
    /// points for simple and mutual recursion generation.
    /// </summary>
    internal class CalculatorClassGenerator
    {
        private readonly GeneratorExecutionContext _context;

        private readonly RecursiveCalculatorClassInfo _classInfo;
        private readonly IReadOnlyList<RecursiveMethodInfo> _methodInfos;

        private CalculatorClassGenerator(GeneratorExecutionContext context, RecursiveCalculatorClassInfo classInfo, IReadOnlyList<RecursiveMethodInfo> methodInfos)
        {
            _context = context;
            _classInfo = classInfo;
            _methodInfos = methodInfos;
        }

        internal static string GenerateSimpleSafeRec(GeneratorExecutionContext context, RecursiveMethodInfo methodInfo)
        {
            var classSyntax = (ClassDeclarationSyntax) methodInfo.MethodSyntax.Parent;
            var classSymbol = methodInfo.MethodSymbol.ContainingType;
            var recursionId = methodInfo.MethodSymbol.Name;
            var classInfo = new RecursiveCalculatorClassInfo(classSyntax, classSymbol, isMultiSite: false, recursionId,
                methodInfo.GenericParams, methodInfo.ConstraintClauses, methodInfo.MethodParamsStructName);

            return new CalculatorClassGenerator(context, classInfo, new List<RecursiveMethodInfo>() {methodInfo}).GenerateAll();
        }

        internal static string GenerateMutualSafeRec(GeneratorExecutionContext context, IReadOnlyList<RecursiveMethodInfo> methodInfos)
        {
            var firstMethodInfo = methodInfos.First();
            if (string.IsNullOrEmpty(firstMethodInfo.CodeGenProps.MutualRecursionId))
                throw new ArgumentException($"Empty {nameof(SafeRecursionCodeGenProps.MutualRecursionId)} in the first methodInfo of {nameof(methodInfos)}.");

            if (methodInfos.Count == 1)
            {
                // Warn the user but continue anyway as we should generate a working code anyway if there
                // is actually exactly 1 MutualRecursionId. The code will be slower but it should work.
                context.ReportConfigurationWarning(firstMethodInfo.MethodSyntax,
                    $"Suspiciously only one method is found for the mutual recursive group '{firstMethodInfo.CodeGenProps.MutualRecursionId}'");
            }

            // TODO really check that all GenericParams and ConstraintClauses for all methodInfos are
            // the same. If it is not so, the compilation probably will fail anyway but the message will
            // not be very user-friendly
            foreach (var methodInfo in methodInfos)
            {
                if ((methodInfo.GenericParams != firstMethodInfo.GenericParams) ||
                    (methodInfo.ConstraintClauses != firstMethodInfo.ConstraintClauses))
                {
                    // string equality check is not really correct so only generate a warning
                    context.ReportConfigurationWarning(methodInfo.MethodSyntax,
                        $"Suspiciously generic methods '{firstMethodInfo.MethodName}' and '{methodInfo.MethodName}' have differently looking generic params"
                        + $" '{firstMethodInfo.GenericParams}' '{firstMethodInfo.ConstraintClauses}'"
                        + $" != '{methodInfo.GenericParams}' '{methodInfo.ConstraintClauses}'");
                }
            }

            var classSyntax = (ClassDeclarationSyntax) firstMethodInfo.MethodSyntax.Parent;
            var classSymbol = firstMethodInfo.MethodSymbol.ContainingType;
            var recursionId = firstMethodInfo.CodeGenProps.MutualRecursionId;
            var classInfo = new RecursiveCalculatorClassInfo(classSyntax, classSymbol, isMultiSite: true, recursionId,
                firstMethodInfo.GenericParams, firstMethodInfo.ConstraintClauses,
                paramsStructName: recursionId + RecursiveCalculatorClassInfo.DispatchStructSuffix);

            return new CalculatorClassGenerator(context, classInfo, methodInfos).GenerateAll();
        }

        private string GenerateAll()
        {
            var allCode = new CodeBuilder();
            var classInfo = _classInfo;
            var classSyntax = _classInfo.ContainingClassSyntax;
            var namespaceName = classInfo.ContainingClassSymbol.ContainingNamespace.ToDisplayString();

            var methodGens = _methodInfos.Select(mi => new CalculatorMethodGenerator(_context, classInfo, mi)).ToList();
            var targetMethods = new TargetMethodsInfo(_methodInfos);

            // copy all the usings from the original file
            var root = (CompilationUnitSyntax) classSyntax.SyntaxTree.GetRoot();
            allCode.AddHeaderLine(root.Usings.ToFullString());
            allCode.AddHeaderLine();

            // namespace
            allCode.AddBlockHeader($"namespace {namespaceName}");

            // class declaration
            var classModifiersString = classSyntax.Modifiers.ToFullString();
            if (!classModifiersString.Contains("partial"))
            {
                _context.ReportUnsupportedSyntaxError(classSyntax, $"Target class '{_classInfo.ContainingClassSymbol.Name}' for '{classInfo.RecursionId}' is not partial");
                return null;
            }

            allCode.AddBlockHeader($"{classModifiersString} class {_classInfo.ContainingClassSymbol.Name}");

            allCode.AddHeaderLine();
            allCode.AddHeaderLine(GenerateProxyMethods(methodGens));

            allCode.AddHeaderLine();
            allCode.AddHeaderLine(GenerateParamsStructs(classInfo, methodGens));

            allCode.AddHeaderLine();
            allCode.AddHeaderLine(GenerateCalculatorClass(classInfo, methodGens, targetMethods));

            return allCode.BuildString();
        }

        private string GenerateProxyMethods(List<CalculatorMethodGenerator> methodGens)
        {
            bool alwaysExpose = methodGens.Count == 1;
            var code = new CodeBuilder();
            foreach (var methodGen in methodGens)
            {
                if (alwaysExpose || methodGen.MethodInfo.CodeGenProps.ExposeAsEntryPoint)
                    code.AddHeaderLine(methodGen.GenerateProxyMethod());
            }

            return code.BuildString();
        }

        #region Params structs

        private string GenerateParamsStructs(RecursiveCalculatorClassInfo classInfo, IReadOnlyList<CalculatorMethodGenerator> methodGens)
        {
            var code = new CodeBuilder();

            if (classInfo.IsMultiSite)
            {
                code.AddHeaderLine(GenerateCallSiteEnum(classInfo, methodGens));
                code.AddHeaderLine(GenerateDispatchStruct(classInfo, methodGens));
                code.AddHeaderLine();
            }

            foreach (var methodGen in methodGens)
            {
                code.AddHeaderLine(methodGen.GenerateMethodParamsStruct());
            }

            return code.BuildString();
        }

        private string GenerateDispatchStruct(RecursiveCalculatorClassInfo classInfo, IReadOnlyList<CalculatorMethodGenerator> methodGens)
        {
            var code = new CodeBuilder();

            // generic struct can't have an explicit layout so use just a simple class in such case instead
            bool isGeneric = !string.IsNullOrWhiteSpace(classInfo.GenericParams);
            if (!isGeneric)
            {
                code.AddHeaderLine("\t[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit)]");
                code.AddHeaderLine($"\tprivate struct {classInfo.ParamsStructName} {classInfo.GenericParams}");
            }
            else
            {
                code.AddHeaderLine($"\tprivate class {classInfo.ParamsStructName} {classInfo.GenericParams}");
            }

            if (!string.IsNullOrEmpty(classInfo.ConstraintClauses))
                code.AddHeaderLine($"\t\t{classInfo.ConstraintClauses}");
            code.AddBlockHeader("\t");

            // the call site dispatch enum field
            if (!isGeneric)
                code.AddHeaderLine("\t\t[System.Runtime.InteropServices.FieldOffset(0)]");
            code.AddHeaderLine($"\t\tinternal readonly {classInfo.CallSiteEnumName} {RecursiveCalculatorClassInfo.CallSiteEnumFieldName};");

            // params fields for each method
            foreach (var methodGen in methodGens)
            {
                code.AddHeaderLine();
                if (!isGeneric)
                    code.AddHeaderLine($"\t\t[System.Runtime.InteropServices.FieldOffset(sizeof({classInfo.CallSiteEnumName}))]");
                code.AddHeaderLine(
                    $"\t\tinternal readonly {methodGen.MethodInfo.MethodParamsStructName}{methodGen.MethodInfo.GenericParams} {methodGen.MethodInfo.MethodParamsStructName};");
            }

            // constructors: 1 for each method
            foreach (var methodGen in methodGens)
            {
                var methodInfo = methodGen.MethodInfo;
                // constructor header
                //TODO: improve CodeBuilder to use code.AddBlockHeader() here
                code.AddHeaderLine($"\t\tinternal {classInfo.ParamsStructName}({methodInfo.MethodParamsStructName}{methodInfo.GenericParams} {methodInfo.MethodParamsStructName})");
                if (!isGeneric)
                {
                    code.AddHeaderLine("\t\t\t:this()");
                }

                code.AddHeaderLine("\t\t{");
                // constructor body
                code.AddHeaderLine($"\t\t\tthis.{RecursiveCalculatorClassInfo.CallSiteEnumFieldName} = {classInfo.CallSiteEnumName}.{methodInfo.MethodEnumValueName};");
                code.AddHeaderLine($"\t\t\tthis.{methodInfo.MethodParamsStructName} = {methodInfo.MethodParamsStructName};");
                code.AddHeaderLine("\t\t}");

                // using an implicit operator is a small hack that simplifies code rewriting 
                code.AddHeaderLine(
                    $"\t\tpublic static implicit operator {classInfo.ParamsStructName}{classInfo.GenericParams}({methodInfo.MethodParamsStructName}{methodInfo.GenericParams} {methodInfo.MethodParamsStructName}) "
                    + $" => new {classInfo.ParamsStructName}{classInfo.GenericParams}({methodInfo.MethodParamsStructName});");
            }

            return code.BuildString();
        }

        private string GenerateCallSiteEnum(RecursiveCalculatorClassInfo classInfo, IReadOnlyList<CalculatorMethodGenerator> methodGens)
        {
            var code = new CodeBuilder();

            code.AddBlockHeader($"\tprivate enum {classInfo.CallSiteEnumName} : int");
            foreach (var methodGen in methodGens)
            {
                code.AddHeaderLine($"\t\t{methodGen.MethodInfo.MethodEnumValueName},");
            }

            return code.BuildString();
        }

        #endregion

        #region Calculator class

        private string GenerateCalculatorClass(RecursiveCalculatorClassInfo classInfo, List<CalculatorMethodGenerator> methodGens, TargetMethodsInfo targetMethods)
        {
            if (classInfo.IsMultiSite)
            {
                // mutual recursion with a dispatcher method
                return GenerateMultiSiteCalculatorClass(classInfo, methodGens, targetMethods);
            }
            else
            {
                // simple recursion
                return GenerateSimpleCalculatorClass(classInfo, methodGens, targetMethods);
            }
        }

        private static string GenerateSimpleCalculatorClass(RecursiveCalculatorClassInfo classInfo, List<CalculatorMethodGenerator> methodGens, TargetMethodsInfo targetMethods)
        {
            var code = new CodeBuilder();

            var methodGen = methodGens.Single();
            var methodInfo = methodGen.MethodInfo;
            code.AddBlockHeader(@$"    private class {classInfo.CalculatorClassName}{classInfo.GenericParams} :
                        AntiSO.Infrastructure.SimpleRecursionRunner<{methodInfo.MethodParamsStructName}{methodInfo.GenericParams}>
                        {methodInfo.ConstraintClauses}");

            // single field for the return value
            code.AddHeaderLine($"\tinternal {methodInfo.ReturnType} {methodInfo.ReturnFieldName};");
            code.AddHeaderLine();
            // actual recursive method
            code.AddBlockHeader($"\tprotected override IEnumerator<{methodInfo.MethodParamsStructName}{methodInfo.GenericParams}>"
                                + $" ComputeImpl({classInfo.ParamsStructName}{classInfo.GenericParams} {CalculatorMethodGenerator.ParamsVarName})");
            code.AddHeaderLine(methodGen.GenerateCalculatorMethodBody(targetMethods));
            return code.BuildString();
        }


        private static string GenerateMultiSiteCalculatorClass(RecursiveCalculatorClassInfo classInfo, List<CalculatorMethodGenerator> methodGens, TargetMethodsInfo targetMethods)
        {
            var code = new CodeBuilder();

            // here classInfo.ParamsStructName will be the dispatcher struct
            code.AddBlockHeader(@$"    private class {classInfo.CalculatorClassName}{classInfo.GenericParams} :
                        AntiSO.Infrastructure.SimpleRecursionRunner<{classInfo.ParamsStructName}{classInfo.GenericParams}>
                        {classInfo.ConstraintClauses}");

            // fields for return values
            foreach (var methodGen in methodGens)
            {
                var methodInfo = methodGen.MethodInfo;
                code.AddHeaderLine($"\t\tinternal {methodInfo.ReturnType} {methodInfo.ReturnFieldName};");
            }

            // the dispatcher method
            code.AddHeaderLine();
            code.AddHeaderLine(GenerateCallDispatchMethod(classInfo, methodGens));

            // actual recursive methods
            foreach (var methodGen in methodGens)
            {
                var methodInfo = methodGen.MethodInfo;
                //TODO: improve CodeBuilder to use code.AddBlockHeader() here
                code.AddHeaderLine($"\t\tprivate IEnumerator<{classInfo.ParamsStructName}{classInfo.GenericParams}>"
                                   + $" {methodInfo.MethodName}({methodInfo.MethodParamsStructName}{methodInfo.GenericParams} {CalculatorMethodGenerator.ParamsVarName})");
                code.AddHeaderLine("\t\t{");
                code.AddHeaderLine(methodGen.GenerateCalculatorMethodBody(targetMethods));
                code.AddHeaderLine("\t\t}");
            }

            return code.BuildString();
        }

        private static string GenerateCallDispatchMethod(RecursiveCalculatorClassInfo classInfo, List<CalculatorMethodGenerator> methodGens)
        {
            var code = new CodeBuilder();
            code.AddBlockHeader($"\t\tprotected override IEnumerator<{classInfo.ParamsStructName}{classInfo.GenericParams}>"
                                + $" ComputeImpl({classInfo.ParamsStructName}{classInfo.GenericParams} {CalculatorMethodGenerator.ParamsVarName})");
            code.AddBlockHeader($"\t\t\tswitch ({CalculatorMethodGenerator.ParamsVarName}.{RecursiveCalculatorClassInfo.CallSiteEnumFieldName})");
            foreach (var methodGen in methodGens)
            {
                var methodInfo = methodGen.MethodInfo;
                code.AddHeaderLine($"\t\t\t\tcase {classInfo.CallSiteEnumName}.{methodInfo.MethodEnumValueName}:");
                code.AddHeaderLine($"\t\t\t\t\treturn {methodInfo.MethodName}({CalculatorMethodGenerator.ParamsVarName}.{methodInfo.MethodParamsStructName});");
            }

            code.AddHeaderLine("\t\t\t\tdefault:");
            code.AddHeaderLine("\t\t\t\t\t// this should never happen as the switch is exhaustive");
            code.AddHeaderLine(
                $"\t\t\t\t\tthrow new AntiSO.Infrastructure.AntiSOBadSwitchException($\"Unexpected method call {{{CalculatorMethodGenerator.ParamsVarName}.{RecursiveCalculatorClassInfo.CallSiteEnumFieldName}}}\");");

            return code.BuildString();
        }

        #endregion
    }
}