using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace AntiSO.CodeGen
{
    [Generator]
    public class SafeRecursionGenerator : ISourceGenerator
    {
        private static readonly Type TargetAttribute = typeof(GenerateSafeRecursionAttribute);
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
                return (methodSyntax, methodSymbol, methodModel, attr);
            }).Where(tuple =>
            {
                var (_, _, _, attr) = tuple;
                return attr != null;
            }).ToList();
            context.Log($"Found {goodCandidates.Count} method(s) for recursion generation.");

            foreach (var (methodSyntax, methodSymbol, methodModel, attr) in goodCandidates)
            {
                try
                {
                    var codeGenProps = SafeRecursionCodeGenProps.ParseFromAttribute(context, methodSyntax, attr);
                    var generatedCode = CodeGenerator.GenerateSafeRec(context, methodSyntax, methodSymbol, methodModel, codeGenProps);
                    if (generatedCode != null)
                    {
                        context.AddSource($"{methodSymbol.ContainingType.Name}_{methodSymbol.Name}_SafeRecursion.cs", SourceText.From(generatedCode, Encoding.UTF8));
                    }
                }
                catch (Exception e)
                {
                    context.LogInternalError(methodSyntax.GetLocation(), $"Processing '{methodSymbol.ContainingType.Name}' resulted in an internal error '{e}'");
                }
            }
        }


        class CodeGenerator
        {
            private readonly GeneratorExecutionContext _context;
            private readonly MethodDeclarationSyntax _methodSyntax;
            private readonly IMethodSymbol _methodSymbol;
            private readonly SemanticModel _methodModel;
            private readonly SafeRecursionCodeGenProps _codeGenProps;

            CodeGenerator(GeneratorExecutionContext context, MethodDeclarationSyntax methodSyntax, IMethodSymbol methodSymbol, SemanticModel methodModel,
                SafeRecursionCodeGenProps codeGenProps)
            {
                _methodSyntax = methodSyntax;
                _methodSymbol = methodSymbol;
                _methodModel = methodModel;
                _codeGenProps = codeGenProps;
                _context = context;
            }

            internal static string GenerateSafeRec(GeneratorExecutionContext context, MethodDeclarationSyntax methodSyntax, IMethodSymbol methodSymbol, SemanticModel methodModel,
                SafeRecursionCodeGenProps codeGenProps)
            {
                return new CodeGenerator(context, methodSyntax, methodSymbol, methodModel, codeGenProps).GenerateAll();
            }

            private string GenerateAll()
            {
                var allCode = new CodeBuilder();
                var classSyntax = (ClassDeclarationSyntax) _methodSyntax.Parent;
                var classSymbol = _methodSymbol.ContainingType;
                var namespaceName = classSymbol.ContainingNamespace.ToDisplayString();

                var root = (CompilationUnitSyntax) classSyntax.SyntaxTree.GetRoot();
                // code all the using from the original file
                allCode.AddHeaderLine(root.Usings.ToFullString());
                allCode.AddHeaderLine();
                // explicitly add the usings we use
                allCode.AddHeaderLine("using System;");
                allCode.AddHeaderLine("using System.Collections.Generic;");
                allCode.AddHeaderLine();

                allCode.AddBlockHeader($"namespace {namespaceName}");
                var classModifiers = classSyntax.Modifiers.ToFullString();
                if (!classModifiers.Contains("partial"))
                {
                    _context.ReportUnsupportedSyntaxError(classSyntax, $"Target class '{classSymbol.Name}' for '{_methodSymbol.Name}' is not partial");
                    return null;
                }

                allCode.AddBlockHeader($"{classModifiers} class {classSymbol.Name}");

                allCode.AddHeaderLine();

                allCode.AddHeaderLine(GenerateParamsStruct());
                allCode.AddHeaderLine(GenerateProxyMethod());

                allCode.AddHeaderLine();
                allCode.AddHeaderLine(GenerateCalculatorClass());

                return allCode.BuildString();
            }

            /// <summary>
            /// The name of the protected field in the <see cref="AntiSO.Infrastructure.SimpleRecursionRunner{TCallParams,TResult}"/>
            /// </summary>
            const string GlobalResVar = "_lastReturnValue";

            const string ParamsVarName = "callParams";
            private string ParamsStructName => _methodSymbol.Name + "_RecCallParams";

            private string CalculatorClassName => _methodSymbol.Name + "_RecRunner";
            private string GenericParams => _methodSyntax.TypeParameterList?.ToFullString() ?? "";
            private string ConstraintClauses => _methodSyntax.ConstraintClauses.ToFullString();
            private string ReturnType => _methodSyntax.ReturnType.ToFullString();

            private string GenerateParamsStruct()
            {
                var code = new CodeBuilder();
                code.AddBlockHeader($"\tprivate struct {ParamsStructName} {GenericParams}" +
                                    $"\n{ConstraintClauses}");

                var paramsSymbols = _methodSyntax.ParameterList.Parameters
                    .Select(parameterSyntax => _methodModel.GetDeclaredSymbol(parameterSyntax))
                    .ToList();

                // fields
                code.AddHeaderLine(string.Join("\n",
                    paramsSymbols.Select(paramSymbol => $"\t\tinternal readonly {paramSymbol.Type.ToDisplayString()} {paramSymbol.Name};")));
                code.AddHeaderLine();

                // constructor header
                code.AddBlockHeader($"\t\tinternal {ParamsStructName}(" + string.Join(", ",
                    paramsSymbols.Select(paramSymbol => $"{paramSymbol.Type.ToDisplayString()} {paramSymbol.Name}")) + ")");
                // constructor body
                code.AddHeaderLine(string.Join("\n",
                    paramsSymbols.Select(paramSymbol => $"\t\t\tthis.{paramSymbol.Name} = {paramSymbol.Name};")));

                return code.BuildString();
            }

            private string GenerateProxyMethod()
            {
                var code = new CodeBuilder();

                code.AddHeaderLine("\t//Proxy method");
                // copy all attributes as is except for our target attribute
                foreach (var attrListSyntax in _methodSyntax.AttributeLists)
                {
                    var rawAttrList = attrListSyntax.Attributes
                        .Where(asy =>
                        {
                            var attrConstrSymbol = (IMethodSymbol) _methodModel.GetSymbolInfo(asy).Symbol;
                            ITypeSymbol? attrSymbol = attrConstrSymbol.ReceiverType;
                            return !attrSymbol.MatchesRuntimeType(TargetAttribute);
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

                // modifiers
                code.AddHeader("\t");
                if (_codeGenProps.AccessLevel == AccessLevel.CopyExisting)
                {
                    code.AddHeader(_methodSyntax.Modifiers.ToFullString().Trim());
                }
                else
                {
                    code.AddHeader(_codeGenProps.GetAccessLevelModifierString() + " ");
                    foreach (var modifier in _methodSyntax.Modifiers)
                    {
                        switch (modifier.Kind())
                        {
                            case SyntaxKind.PublicKeyword:
                            case SyntaxKind.PrivateKeyword:
                            case SyntaxKind.ProtectedKeyword:
                            case SyntaxKind.InternalKeyword:
                                // do not copy
                                break;
                            default:
                                code.AddHeader(modifier + " ");
                                break;
                        }
                    }
                }

                // method signature itself
                code.AddHeader(" ");
                code.AddHeader(ReturnType);
                string methodName = _codeGenProps.GeneratedMethodName ?? _methodSyntax.Identifier.ToString() + GenerateSafeRecursionAttribute.DefaultNameSuffix;
                code.AddHeader(methodName);
                code.AddHeader(GenericParams);
                var paramsList = _methodSyntax.ParameterList;
                if (ExtensionMethod.CopyExisting != _codeGenProps.ExtensionMethod)
                {
                    var origFirstParam = paramsList.Parameters[0];
                    SyntaxToken? origThisModifier = origFirstParam.Modifiers.FirstOrDefault(m => m.Kind() == SyntaxKind.ThisKeyword);
                    var newModifiers = origFirstParam.Modifiers;
                    if (origThisModifier.HasValue)
                        newModifiers = newModifiers.Remove(origThisModifier.Value);
                    if (ExtensionMethod.Extenstion == _codeGenProps.ExtensionMethod)
                    {
                        newModifiers = newModifiers.Insert(0, SyntaxFactory.Token(SyntaxKind.ThisKeyword).WithTrailingTrivia(SyntaxFactory.Whitespace(" ")));
                    }

                    var newFirstParam = origFirstParam.Update(origFirstParam.AttributeLists, newModifiers, origFirstParam.Type, origFirstParam.Identifier, origFirstParam.Default);
                    var newParams = paramsList.Parameters.Replace(origFirstParam, newFirstParam);
                    paramsList = paramsList.Update(paramsList.OpenParenToken, newParams, paramsList.CloseParenToken);
                }

                code.AddHeader(paramsList.ToFullString());


                code.AddHeader(ConstraintClauses);
                code.AddBlockHeader("\t");

                // method body
                // create the initial CallParams struct
                code.AddHeader($"\t\tvar {ParamsVarName} = new {ParamsStructName}{GenericParams} (")
                    .AddHeader(string.Join(", ",
                        _methodSyntax.ParameterList.Parameters.Select(parameterSyntax => _methodModel.GetDeclaredSymbol(parameterSyntax).Name)))
                    .AddHeaderLine(");");

                code.AddHeaderLine($"\t\treturn new {CalculatorClassName}{GenericParams}().Calculate({ParamsVarName});");

                return code.BuildString();
            }


            private string GenerateCalculatorClass()
            {
                var code = new CodeBuilder();
                code.AddBlockHeader(@$"    private class {CalculatorClassName}{GenericParams} :
                        AntiSO.Infrastructure.SimpleRecursionRunner<{ParamsStructName}{GenericParams}, {ReturnType}>
                        {ConstraintClauses}");

                code.AddHeaderLine($"\t\tinternal {ReturnType} Calculate({ParamsStructName}{GenericParams} {ParamsVarName}) {{");
                code.AddHeaderLine($"\t\t\treturn RunRecursion({ParamsVarName});");
                code.AddHeaderLine("\t\t}");

                code.AddBlockHeader($"\t\tprotected override IEnumerator<{ParamsStructName}{GenericParams}>"
                                    + $" ComputeImpl({ParamsStructName}{GenericParams} {ParamsVarName})");


                // unpack params
                code.AddHeaderLine(string.Join("\n",
                    _methodSyntax.ParameterList.Parameters.Select(parameterSyntax =>
                    {
                        var paramSymbol = _methodModel.GetDeclaredSymbol(parameterSyntax);
                        return $"\t\tvar {paramSymbol.Name} = {ParamsVarName}.{paramSymbol.Name};";
                    })));

                // find all recursive invocations 
                var nodesToReplace = CollectInvocationsWalker.CollectInvocations(_context, _methodSyntax, _methodSymbol, _methodModel);
                if (nodesToReplace.ContainsCriticalFailure)
                {
                    _context.Log(_methodSyntax.GetLocation(), $"An unsupported code was found for '{_methodSymbol.Name}'. Abort processing.");
                    return null;
                }

                _context.Log(_methodSyntax.GetLocation(), $"'{_methodSymbol.Name}':" +
                                                          $" {nodesToReplace.Assignments.Count} assignment(s)" +
                                                          $", {nodesToReplace.DeclarationAndAssignments.Count} var declaration(s)" +
                                                          $", {nodesToReplace.ReturnsRecursive.Count} recursive call(s) in return(s)" +
                                                          $", {nodesToReplace.Returns.Count} return(s)");


                // process all supported kinds of recursive invocations 
                var allNodesToReplace = nodesToReplace.Returns.Cast<StatementSyntax>()
                    .Concat(nodesToReplace.ReturnsRecursive)
                    .Concat(nodesToReplace.Assignments.Select((t) => t.containingStatement))
                    .Concat(nodesToReplace.DeclarationAndAssignments.Select(t => t.containingStatement));
                var newRoot = _methodSyntax.Body.ReplaceNodes(allNodesToReplace, (origNode, curNode) =>
                {
                    // _context.Log("!! " + origNode.Kind() + " " + origNode.ToFullStringTrimmed());
                    switch (origNode.Kind())
                    {
                        case SyntaxKind.ReturnStatement:
                            return ReplaceReturn((ReturnStatementSyntax) origNode, (ReturnStatementSyntax) curNode);

                        case SyntaxKind.ExpressionStatement:
                            return ReplaceAssignment((ExpressionStatementSyntax) origNode, (ExpressionStatementSyntax) curNode);

                        case SyntaxKind.LocalDeclarationStatement:
                            return ReplaceLocalVarDeclrAssignment((LocalDeclarationStatementSyntax) origNode, (LocalDeclarationStatementSyntax) curNode);

                        default:
                            throw new AntiSOGenException($"Unexpected statement kind {origNode.Kind()} to replace {origNode.ToFullStringTrimmed()}");
                    }
                });

                code.AddHeaderLine();
                code.AddHeaderLine();
                code.AddHeaderLine(newRoot.ToFullString());
                code.AddHeaderLine();
                code.AddHeaderLine();

                return code.BuildString();
            }

            private static BlockSyntax GetBlockFromCodeString(SyntaxNode origNode, string code, bool removeBraces = false)
            {
                var origCode = origNode.ToFullStringTrimmed().Replace("\n", "\n//");
                var wrappedCode =
                    $@"                    // Replace '{origCode}'
                    {{
                    {code}
                    // End replace '{origCode}'
                    }}
                    ";
                if (removeBraces)
                {
                    // add an empty ";" for the "end" comment to survive when we remove the braces
                    wrappedCode += ";";
                }

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

            private SyntaxNode ReplaceReturn(ReturnStatementSyntax origNode, ReturnStatementSyntax curNode)
            {
                //Support cases of "return RecursiveCall(different params)" like GCD
                if (curNode.Expression.Kind() == SyntaxKind.InvocationExpression)
                {
                    var inv = (InvocationExpressionSyntax) curNode.Expression;
                    if (IsInvocationRecursiveCall(inv))
                    {
                        // because of the way we handle return value now
                        // we can just pass it through here
                        return GetBlockFromCodeString(origNode, @$"
                            yield return new {ParamsStructName}{GenericParams}{inv.ArgumentList.ToFullString()};
                            yield break;");
                    }
                }

                return GetBlockFromCodeString(origNode, @$"
                    {GlobalResVar} = {origNode.Expression.ToFullString()};
                    yield break;");
            }

            private SyntaxNode ReplaceAssignment(ExpressionStatementSyntax origNode, ExpressionStatementSyntax curNode)
            {
                var assignment = (AssignmentExpressionSyntax) origNode.Expression;
                var inv = (InvocationExpressionSyntax) assignment.Right;
                var ident = (IdentifierNameSyntax) assignment.Left;
                return GetBlockFromCodeString(origNode, @$"
                    yield return new {ParamsStructName}{GenericParams}{inv.ArgumentList.ToFullString()};
                    {ident.ToFullString()} = {GlobalResVar};");
            }

            private bool IsInvocationRecursiveCall(InvocationExpressionSyntax inv)
            {
                var invokeInfo = _methodModel.GetSymbolInfo(inv);
                return _methodSymbol.Equals(invokeInfo.Symbol, SymbolEqualityComparer.Default);
            }

            private bool IsDeclaratorContainsRecursiveCall(VariableDeclaratorSyntax dcl)
            {
                if (dcl.Initializer == null)
                    return false;
                if (dcl.Initializer.Value.Kind() != SyntaxKind.InvocationExpression)
                    return false;

                var inv = (InvocationExpressionSyntax) dcl.Initializer.Value;
                return IsInvocationRecursiveCall(inv);
            }

            private SyntaxNode ReplaceLocalVarDeclrAssignment(LocalDeclarationStatementSyntax origNode, LocalDeclarationStatementSyntax curNode)
            {
                var code = new CodeBuilder();
                var declaration = curNode.Declaration;
                // handle each variable in the declaration and split them into 
                // one declaration per variable modifying the ones that use the recursive call
                foreach (var declarator in declaration.Variables)
                {
                    var varIdent = declarator.Identifier;
                    if (IsDeclaratorContainsRecursiveCall(declarator))
                    {
                        var inv = (InvocationExpressionSyntax) declarator.Initializer.Value;
                        code.AddHeaderLine(@$"
                            yield return new {ParamsStructName}{GenericParams}{inv.ArgumentList.ToFullString()};
                            {declaration.Type} {varIdent.ToFullString()} = {GlobalResVar};");
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
                return GetBlockFromCodeString(origNode, code.BuildString(), removeBraces: true);
            }
        }


        private class NodesToReplace
        {
            public bool ContainsCriticalFailure = false;

            public readonly List<(AssignmentExpressionSyntax assignment, ExpressionStatementSyntax containingStatement, BlockSyntax containingBlock)> Assignments =
                new List<(AssignmentExpressionSyntax, ExpressionStatementSyntax, BlockSyntax )>();

            public readonly List<(VariableDeclaratorSyntax varDeclarator, LocalDeclarationStatementSyntax containingStatement, SyntaxNode containingBlock)>
                DeclarationAndAssignments =
                    new List<(VariableDeclaratorSyntax varDeclarator, LocalDeclarationStatementSyntax containingStatement, SyntaxNode containingBlock)>();


            public readonly List<ReturnStatementSyntax> ReturnsRecursive = new List<ReturnStatementSyntax>();

            public readonly List<ReturnStatementSyntax> Returns = new List<ReturnStatementSyntax>();
        }

        private class CollectInvocationsWalker : CSharpSyntaxWalker
        {
            private readonly GeneratorExecutionContext _context;
            private readonly IMethodSymbol _methodSymbol;

            private readonly SemanticModel _methodModel;
            private readonly NodesToReplace _nodesToReplace = new NodesToReplace();

            private CollectInvocationsWalker(GeneratorExecutionContext context, IMethodSymbol methodSymbol, SemanticModel methodModel)
            {
                _context = context;
                _methodSymbol = methodSymbol;
                _methodModel = methodModel;
            }

            internal static NodesToReplace CollectInvocations(GeneratorExecutionContext context, MethodDeclarationSyntax methodSyntax, IMethodSymbol methodSymbol,
                SemanticModel methodModel)
            {
                var walker = new CollectInvocationsWalker(context, methodSymbol, methodModel);
                walker.Visit(methodSyntax.Body);
                return walker._nodesToReplace;
            }

            public override void VisitReturnStatement(ReturnStatementSyntax node)
            {
                base.VisitReturnStatement(node);
                _nodesToReplace.Returns.Add(node);
            }

            public override void VisitInvocationExpression(InvocationExpressionSyntax node)
            {
                base.VisitInvocationExpression(node);
                var invokeInfo = _methodModel.GetSymbolInfo(node);
                if (_methodSymbol.Equals(invokeInfo.Symbol, SymbolEqualityComparer.Default))
                {
                    if (!AddInvocation(node))
                        _nodesToReplace.ContainsCriticalFailure = true;
                }
            }

            private bool AddInvocation(InvocationExpressionSyntax inv)
            {
                var parent = inv.Parent;
                if (parent == null)
                {
                    _context.ReportUnsupportedSyntaxError(inv, $"Unexpected location (no parent) for recursive invocation '{inv.ToFullStringTrimmed()}'");
                    return false;
                }

                switch (parent.Kind())
                {
                    case SyntaxKind.SimpleAssignmentExpression:
                    {
                        var assignment = (AssignmentExpressionSyntax) parent;
                        // _context.Log(assignment.GetLocation(), "" + assignment.Left.Kind());
                        // _context.Log(assignment.GetLocation(), "" + assignment.Left.GetType().FullName);
                        // _context.Log(assignment.GetLocation(), assignment.ToFullStringTrimmed());
                        // _context.Log(assignment.GetLocation(), "" + assignment.Parent.Kind());
                        // _context.Log(assignment.GetLocation(), "" + assignment.Parent.ToFullStringTrimmed());
                        // _context.Log(assignment.GetLocation(), "" + assignment.Parent.Parent.Kind());
                        var assignmentParent = assignment.Parent;
                        if (assignmentParent.Kind() != SyntaxKind.ExpressionStatement)
                        {
                            _context.ReportUnsupportedSyntaxError(assignmentParent,
                                $"Unsupported parent for recursive invocation {assignmentParent.Kind()}: '{assignmentParent.ToFullStringTrimmed()}'");
                            return false;
                        }

                        var containingStatement = (ExpressionStatementSyntax) assignmentParent;
                        var expressionParent = containingStatement.Parent;
                        // TODO: potentially we can introduce a block ourselves here
                        if (expressionParent.Kind() != SyntaxKind.Block)
                        {
                            _context.ReportUnsupportedSyntaxError(expressionParent,
                                $"Recursive invocation should be in a block {expressionParent.Kind()}: '{expressionParent.ToFullStringTrimmed()}'");
                            return false;
                        }

                        var block = (BlockSyntax) expressionParent;
                        _nodesToReplace.Assignments.Add((assignment, containingStatement, block));
                        return true;
                    }

                    case SyntaxKind.EqualsValueClause:
                    {
                        var equalsValueSyntax = (EqualsValueClauseSyntax) parent;
                        var varDeclarator = (VariableDeclaratorSyntax) equalsValueSyntax.Parent;
                        var varDeclaration = (VariableDeclarationSyntax) varDeclarator.Parent;
                        // TODO: support other cases such as for, foreach,...
                        // the containingStatement is expected to be something like LocalDeclarationStatementSyntax
                        // var containingStatement = (StatementSyntax) varDeclaration.Parent;
                        var containingStatement = varDeclaration.Parent;
                        if (containingStatement.Kind() != SyntaxKind.LocalDeclarationStatement)
                        {
                            _context.ReportUnsupportedSyntaxError(containingStatement,
                                $"Unsupported location for recursive invocation {containingStatement.Kind()}: '{inv.ToFullStringTrimmed()}'");
                            return false;
                        }

                        var localDeclStatement = (LocalDeclarationStatementSyntax) containingStatement;
                        var containingBlockCandidate = localDeclStatement.Parent;
                        if (containingBlockCandidate.Kind() != SyntaxKind.Block)
                        {
                            _context.ReportUnsupportedSyntaxError(containingBlockCandidate,
                                $"Unsupported location for recursive invocation {containingBlockCandidate.Kind()}: '{inv.ToFullStringTrimmed()}'");
                            return false;
                        }

                        var containingBlock = containingBlockCandidate;
                        _nodesToReplace.DeclarationAndAssignments.Add((varDeclarator, localDeclStatement, containingBlock));
                        return true;
                    }

                    case SyntaxKind.ReturnStatement:
                    {
                        var returnStatement = (ReturnStatementSyntax) parent;
                        _nodesToReplace.ReturnsRecursive.Add(returnStatement);
                        return true;
                    }

                    default:
                        _context.ReportUnsupportedSyntaxError(inv, $"Unsupported location for recursive invocation {parent.Kind()}: '{parent.ToFullStringTrimmed()}'");
                        return false;
                }
            }

            public override void VisitThrowStatement(ThrowStatementSyntax node)
            {
                base.VisitThrowStatement(node);
                _context.ReportUnsupportedSyntaxWarning(node,
                    $"Throwing and catching exceptions is not fully supported yet. The code might produce unexpected results '{node.ToFullStringTrimmed()}'");
            }

            public override void VisitCatchDeclaration(CatchDeclarationSyntax node)
            {
                base.VisitCatchDeclaration(node);
                _context.ReportUnsupportedSyntaxWarning(node,
                    $"Throwing and catching exceptions is not fully supported yet. The code might produce unexpected results. '{node.ToFullStringTrimmed()}'");
            }

            public override void VisitFinallyClause(FinallyClauseSyntax node)
            {
                base.VisitFinallyClause(node);
                _context.ReportUnsupportedSyntaxWarning(node,
                    $"Throwing and catching exceptions is not fully supported yet. The code might produce unexpected results. '{node.ToFullStringTrimmed()}'");
            }

            public override void VisitYieldStatement(YieldStatementSyntax node)
            {
                base.VisitYieldStatement(node);
                _nodesToReplace.ContainsCriticalFailure = true;
                _context.ReportUnsupportedSyntaxError(node, $"yield return inside recursive functions is not supported: '{node.ToFullStringTrimmed()}'");
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