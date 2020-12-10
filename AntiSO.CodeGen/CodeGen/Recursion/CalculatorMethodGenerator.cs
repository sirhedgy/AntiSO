using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AntiSO.CodeGen.Recursion
{
    internal sealed class RecursiveMethodInfo
    {
        public readonly MethodDeclarationSyntax MethodSyntax;
        public readonly IMethodSymbol MethodSymbol;
        public readonly SemanticModel MethodModel;
        public readonly SafeRecursionCodeGenProps CodeGenProps;

        internal string MethodName => CodeGenProps.GeneratedMethodName ?? MethodSyntax.Identifier + GenerateSafeRecursionAttribute.DefaultNameSuffix;
        internal string MethodEnumValueName => MethodName;
        internal string MethodParamsStructName => MethodName + "_MethodRecCallParams";

        internal string ReturnFieldName => $"_{MethodName}_ReturnValue";
        internal bool ShouldGenerateReturnField => !MethodSymbol.ReturnsVoid;
        internal string ReturnType => MethodSyntax.ReturnType.ToFullString();
        internal string GenericParams => MethodSyntax.TypeParameterList?.ToFullString() ?? "";
        internal string ConstraintClauses => MethodSyntax.ConstraintClauses.ToFullString();

        public RecursiveMethodInfo(MethodDeclarationSyntax methodSyntax, IMethodSymbol methodSymbol, SemanticModel methodModel, SafeRecursionCodeGenProps codeGenProps)
        {
            MethodSyntax = methodSyntax;
            MethodSymbol = methodSymbol;
            MethodModel = methodModel;
            CodeGenProps = codeGenProps;
        }
    }

    /// <summary>
    /// This is the class that makes the main transformation of the recursive function into a
    /// stack-safe version. It uses <see cref="CollectInvocationsWalker"/> to find all places
    /// to change the code and then generates a new <see cref="SyntaxNode"/>
    /// </summary>
    /// <seealso cref="CalculatorClassGenerator"/>
    internal class CalculatorMethodGenerator
    {
        internal const string ParamsVarName = "callParams";

        private readonly GeneratorExecutionContext _context;

        private readonly RecursiveCalculatorClassInfo _calculatorClassInfo;
        private readonly RecursiveMethodInfo _methodInfo;

        internal RecursiveMethodInfo MethodInfo => _methodInfo;

        internal CalculatorMethodGenerator(GeneratorExecutionContext context, RecursiveCalculatorClassInfo calculatorClassInfo, RecursiveMethodInfo methodInfo)
        {
            _context = context;
            _calculatorClassInfo = calculatorClassInfo;
            _methodInfo = methodInfo;
        }

        internal string GenerateMethodParamsStruct()
        {
            var code = new CodeBuilder();
            code.AddHeaderLine($"\tprivate struct {_methodInfo.MethodParamsStructName} {_methodInfo.GenericParams}");
            if (!string.IsNullOrEmpty(_methodInfo.ConstraintClauses))
                code.AddHeaderLine($"\t\t{_methodInfo.ConstraintClauses}");
            code.AddBlockHeader("\t");

            var paramsSymbols = _methodInfo.MethodSyntax.ParameterList.Parameters
                .Select(parameterSyntax => (IParameterSymbol) _methodInfo.MethodModel.GetDeclaredSymbol(parameterSyntax))
                .ToList();

            // fields
            code.AddHeaderLine(string.Join("\n",
                paramsSymbols.Select(paramSymbol => $"\t\tinternal readonly {paramSymbol.Type.ToDisplayString()} {paramSymbol.Name};")));
            code.AddHeaderLine();

            // constructor header
            code.AddBlockHeader($"\t\tinternal {_methodInfo.MethodParamsStructName}(" + string.Join(", ",
                paramsSymbols.Select(paramSymbol => $"{paramSymbol.Type.ToDisplayString()} {paramSymbol.Name}")) + ")");
            // constructor body
            code.AddHeaderLine(string.Join("\n",
                paramsSymbols.Select(paramSymbol => $"\t\t\tthis.{paramSymbol.Name} = {paramSymbol.Name};")));

            return code.BuildString();
        }

        #region Proxy method

        internal string GenerateProxyMethod()
        {
            var code = new CodeBuilder();

            code.AddHeaderLine("\t//Proxy method");
            GenerateProxyMethodAttributes(code);
            GenerateProxyMethodModifiers(code);
            // method signature itself
            GenerateProxyMethodSignature(code);
            GenerateProxyMethodBody(code);

            return code.BuildString();
        }


        private void GenerateProxyMethodAttributes(CodeBuilder code)
        {
            // copy all attributes as is except for our target attribute
            foreach (var attrListSyntax in _methodInfo.MethodSyntax.AttributeLists)
            {
                var rawAttrList = attrListSyntax.Attributes
                    .Where(asy =>
                    {
                        var attrConstrSymbol = (IMethodSymbol) _methodInfo.MethodModel.GetSymbolInfo(asy).Symbol;
                        ITypeSymbol? attrSymbol = attrConstrSymbol.ReceiverType;
                        return !attrSymbol.MatchesRuntimeType(SafeRecursionGenerator.TargetAttribute);
                    }).ToList();
                if (rawAttrList.Any())
                {
                    // calling GetSeparators on the original list is a hack but seems to work so far
                    var attrList = SyntaxFactory.SeparatedList(rawAttrList, attrListSyntax.Attributes.GetSeparators());
                    // remove the trivia like #region that might be attached to the first attribute
                    var attrs = attrListSyntax.Update(attrListSyntax.OpenBracketToken, attrListSyntax.Target, attrList, attrListSyntax.CloseBracketToken).WithoutTrivia();
                    code.AddHeaderLine("\t" + attrs.ToFullString());
                }
            }
        }

        private void GenerateProxyMethodModifiers(CodeBuilder code)
        {
            // modifiers
            code.AddHeader("\t");
            if (_methodInfo.CodeGenProps.AccessLevel == AccessLevel.CopyExisting)
            {
                code.AddHeader(_methodInfo.MethodSyntax.Modifiers.ToFullString().Trim());
            }
            else
            {
                code.AddHeader(_methodInfo.CodeGenProps.GetAccessLevelModifierString() + " ");
                foreach (var modifier in _methodInfo.MethodSyntax.Modifiers)
                {
                    switch (modifier.Kind())
                    {
                        case SyntaxKind.PublicKeyword:
                        case SyntaxKind.PrivateKeyword:
                        case SyntaxKind.ProtectedKeyword:
                        case SyntaxKind.InternalKeyword:
                            // do not copy access modifiers
                            break;
                        default:
                            code.AddHeader(modifier + " ");
                            break;
                    }
                }
            }
        }


        private void GenerateProxyMethodSignature(CodeBuilder code)
        {
            code.AddHeader(" ");
            code.AddHeader(_methodInfo.ReturnType);
            code.AddHeader(_methodInfo.MethodName);
            code.AddHeader(_methodInfo.GenericParams);

            code.AddHeader(GetProxyMethodParamsList().ToFullString());

            if (!string.IsNullOrEmpty(_methodInfo.ConstraintClauses))
            {
                code.AddHeader("\t\t");
                code.AddHeaderLine(_methodInfo.ConstraintClauses);
            }
        }

        private ParameterListSyntax GetProxyMethodParamsList()
        {
            var paramsList = _methodInfo.MethodSyntax.ParameterList;
            if (ExtensionMethod.CopyExisting != _methodInfo.CodeGenProps.ExtensionMethod)
            {
                var origFirstParam = paramsList.Parameters[0];
                SyntaxToken? origThisModifier = origFirstParam.Modifiers.FirstOrDefault(m => m.Kind() == SyntaxKind.ThisKeyword);
                var newModifiers = origFirstParam.Modifiers;
                if (origThisModifier.HasValue)
                    newModifiers = newModifiers.Remove(origThisModifier.Value);
                if (ExtensionMethod.Extenstion == _methodInfo.CodeGenProps.ExtensionMethod)
                {
                    newModifiers = newModifiers.Insert(0, SyntaxFactory.Token(SyntaxKind.ThisKeyword).WithTrailingTrivia(SyntaxFactory.Whitespace(" ")));
                }

                var newFirstParam = origFirstParam.Update(origFirstParam.AttributeLists, newModifiers, origFirstParam.Type, origFirstParam.Identifier, origFirstParam.Default);
                var newParams = paramsList.Parameters.Replace(origFirstParam, newFirstParam);
                paramsList = paramsList.Update(paramsList.OpenParenToken, newParams, paramsList.CloseParenToken);
            }

            return paramsList;
        }

        private void GenerateProxyMethodBody(CodeBuilder code)
        {
            // method body
            code.AddBlockHeader("\t");
            // create the initial CallParams struct
            code.AddHeader($"\t\tvar {ParamsVarName} = new {_methodInfo.MethodParamsStructName}{_methodInfo.GenericParams} (")
                .AddHeader(string.Join(", ",
                    _methodInfo.MethodSyntax.ParameterList.Parameters.Select(parameterSyntax => _methodInfo.MethodModel.GetDeclaredSymbol(parameterSyntax).Name)))
                .AddHeaderLine(");");

            code.AddHeaderLine($"\t\tvar runner = new {_calculatorClassInfo.CalculatorClassName}{_calculatorClassInfo.GenericParams}();");
            code.AddHeaderLine($"\t\trunner.RunRecursion({ParamsVarName});");
            if (_methodInfo.ShouldGenerateReturnField)
            {
                code.AddHeaderLine($"\t\treturn runner.{_methodInfo.ReturnFieldName};");
            }
        }

        #endregion


        #region Calculator method

        internal string GenerateCalculatorMethodBody(TargetMethodsInfo targetMethods)
        {
            var code = new CodeBuilder();
            // unpack params
            code.AddHeaderLine("\t\t// unpack params");
            code.AddHeaderLine(string.Join("\n",
                _methodInfo.MethodSyntax.ParameterList.Parameters.Select(parameterSyntax =>
                {
                    var paramSymbol = _methodInfo.MethodModel.GetDeclaredSymbol(parameterSyntax);
                    return $"\t\tvar {paramSymbol.Name} = {ParamsVarName}.{paramSymbol.Name};";
                })));

            // find all recursive invocations 
            var nodesToReplace = CollectInvocationsWalker.CollectInvocations(_context, _methodInfo, targetMethods);
            if (nodesToReplace.ContainsCriticalFailure)
            {
                _context.Log(_methodInfo.MethodSyntax.GetLocation(), $"An unsupported code was found for '{_methodInfo.MethodSymbol.Name}'. Abort processing.");
                return null;
            }

            _context.Log(_methodInfo.MethodSyntax.GetLocation(), $"'{_methodInfo.MethodSymbol.Name}':" +
                                                                 $" {nodesToReplace.VoidCalls.Count} void call(s)" +
                                                                 $" {nodesToReplace.Assignments.Count} assignment(s)" +
                                                                 $", {nodesToReplace.DeclarationAndAssignments.Count} var declaration(s)" +
                                                                 $", {nodesToReplace.ReturnsRecursive.Count} recursive call(s) in return(s)" +
                                                                 $", {nodesToReplace.Returns.Count} return(s)");


            // process all supported kinds of recursive invocations 
            var allNodesToReplace = nodesToReplace.Returns.Cast<StatementSyntax>()
                .Concat(nodesToReplace.ReturnsRecursive)
                .Concat(nodesToReplace.VoidCalls.Select(t => t.containingStatement))
                .Concat(nodesToReplace.Assignments.Select((t) => t.containingStatement))
                .Concat(nodesToReplace.DeclarationAndAssignments.Select(t => t.containingStatement));
            var newRoot = _methodInfo.MethodSyntax.Body.ReplaceNodes(allNodesToReplace, (origNode, curNode) =>
            {
                // _context.Log("!! " + origNode.Kind() + " " + origNode.ToFullStringTrimmed());
                switch (origNode.Kind())
                {
                    case SyntaxKind.ReturnStatement:
                        return ReplaceReturn(targetMethods, (ReturnStatementSyntax) origNode, (ReturnStatementSyntax) curNode);

                    case SyntaxKind.ExpressionStatement:
                        var origExpressionNode = (ExpressionStatementSyntax) origNode;
                        var expression = origExpressionNode.Expression;
                        switch (expression.Kind())
                        {
                            case SyntaxKind.InvocationExpression:
                                return ReplaceVoidCall(targetMethods, origExpressionNode, (ExpressionStatementSyntax) curNode);
                            
                            case SyntaxKind.SimpleAssignmentExpression:
                                return ReplaceAssignment(targetMethods, origExpressionNode, (ExpressionStatementSyntax) curNode);
                            
                            default:
                                throw new AntiSOGenException($"Unexpected expression kind {expression.Kind()} to replace {origNode.ToFullStringTrimmed()}");
                        }


                    case SyntaxKind.LocalDeclarationStatement:
                        return ReplaceLocalVarDeclrAssignment(targetMethods, (LocalDeclarationStatementSyntax) origNode, (LocalDeclarationStatementSyntax) curNode);

                    default:
                        throw new AntiSOGenException($"Unexpected statement kind {origNode.Kind()} to replace {origNode.ToFullStringTrimmed()}");
                }
            });

            code.AddHeaderLine("\t\t// method body");
            code.AddHeader(newRoot.ToFullString());
            return code.BuildString();
        }


        private SyntaxNode ReplaceReturn(TargetMethodsInfo targetMethods, ReturnStatementSyntax origNode, ReturnStatementSyntax curNode)
        {
            //Support cases of "return RecursiveCall(different params)" like GCD
            if (curNode.Expression.Kind() == SyntaxKind.InvocationExpression)
            {
                var inv = (InvocationExpressionSyntax) curNode.Expression;
                var targetMethod = GetInvocationRecursiveCallTarget(targetMethods, inv);
                if (targetMethod != null)
                {
                    // only needed for mutual recursion where fields might be different
                    var copyReturnValueLine = (_methodInfo != targetMethod) && _methodInfo.ShouldGenerateReturnField
                        ? $"{_methodInfo.ReturnFieldName} = {targetMethod.ReturnFieldName}; // copy the return value"
                        : "";

                    return SyntaxUtils.GetBlockFromCodeString(origNode, @$"
                            yield return new {targetMethod.MethodParamsStructName}{targetMethod.GenericParams}{inv.ArgumentList.ToFullString()};
                            {copyReturnValueLine}
                            yield break;");
                }
            }

            return SyntaxUtils.GetBlockFromCodeString(origNode, @$"
                    {_methodInfo.ReturnFieldName} = {origNode.Expression.ToFullString()};
                    yield break;");
        }

        private SyntaxNode ReplaceVoidCall(TargetMethodsInfo targetMethods, ExpressionStatementSyntax origNode, ExpressionStatementSyntax curNode)
        {
            var inv = (InvocationExpressionSyntax) origNode.Expression;
            var targetMethod = GetInvocationRecursiveCallTarget(targetMethods, inv);
            return SyntaxUtils.GetBlockFromCodeString(origNode, @$"
                    yield return new {targetMethod.MethodParamsStructName}{targetMethod.GenericParams}{inv.ArgumentList.ToFullString()};");
        }

        private SyntaxNode ReplaceAssignment(TargetMethodsInfo targetMethods, ExpressionStatementSyntax origNode, ExpressionStatementSyntax curNode)
        {
            var assignment = (AssignmentExpressionSyntax) origNode.Expression;
            var inv = (InvocationExpressionSyntax) assignment.Right;
            var targetMethod = GetInvocationRecursiveCallTarget(targetMethods, inv);
            var ident = (IdentifierNameSyntax) assignment.Left;
            return SyntaxUtils.GetBlockFromCodeString(origNode, @$"
                    yield return new {targetMethod.MethodParamsStructName}{targetMethod.GenericParams}{inv.ArgumentList.ToFullString()};
                    {ident.ToFullString()} = {_methodInfo.ReturnFieldName};");
        }


        private RecursiveMethodInfo? GetInvocationRecursiveCallTarget(TargetMethodsInfo targetMethods, InvocationExpressionSyntax inv)
        {
            var invokeInfo = _methodInfo.MethodModel.GetSymbolInfo(inv);
            return targetMethods[invokeInfo.Symbol];
        }

        private RecursiveMethodInfo? IsDeclaratorContainsRecursiveCall(TargetMethodsInfo targetMethods, VariableDeclaratorSyntax dcl)
        {
            if (dcl.Initializer == null)
                return null;
            if (dcl.Initializer.Value.Kind() != SyntaxKind.InvocationExpression)
                return null;

            var inv = (InvocationExpressionSyntax) dcl.Initializer.Value;
            return GetInvocationRecursiveCallTarget(targetMethods, inv);
        }

        private SyntaxNode ReplaceLocalVarDeclrAssignment(TargetMethodsInfo targetMethodsInfo, LocalDeclarationStatementSyntax origNode, LocalDeclarationStatementSyntax curNode)
        {
            var code = new CodeBuilder();
            var declaration = curNode.Declaration;
            // handle each variable in the declaration and split them into 
            // one declaration per variable modifying the ones that use the recursive call
            foreach (var declarator in declaration.Variables)
            {
                var varIdent = declarator.Identifier;
                var targetMethod = IsDeclaratorContainsRecursiveCall(targetMethodsInfo, declarator);
                if (targetMethod != null)
                {
                    var inv = (InvocationExpressionSyntax) declarator.Initializer.Value;
                    code.AddHeaderLine(@$"
                            yield return new {targetMethod.MethodParamsStructName}{targetMethod.GenericParams}{inv.ArgumentList.ToFullString()};
                            {declaration.Type} {varIdent.ToFullString()} = {targetMethod.ReturnFieldName};");
                }
                else
                {
                    code.AddHeaderLine($"{declaration.Type} {declarator.ToFullString()};");
                }
            }

            // This MissingToken looks like a total hack but seems to work.
            // The idea of this hack is that for variable declaration we can't introduce 
            // a real wrapping block because of the visibility but we still want to return
            // a single statement so we produce a fake block with no braces ("{"","}")
            // It seems to work OK at least when we convert this block to string as
            // a new compilation unit (file). 
            return SyntaxUtils.GetBlockFromCodeString(origNode, code.BuildString(), removeBraces: true);
        }

        #endregion
    }
}