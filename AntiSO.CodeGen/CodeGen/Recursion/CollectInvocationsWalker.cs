using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AntiSO.CodeGen.Recursion
{
    /// <summary>
    /// This is effectively the return value of the <see cref="CollectInvocationsWalker"/>
    /// </summary>
    internal class NodesToReplace
    {
        public bool ContainsCriticalFailure = false;

        public readonly List<(InvocationExpressionSyntax invocation, ExpressionStatementSyntax containingStatement)> VoidCalls =
            new List<(InvocationExpressionSyntax invocation, ExpressionStatementSyntax containingStatement)>();

        public readonly List<(AssignmentExpressionSyntax assignment, ExpressionStatementSyntax containingStatement, BlockSyntax containingBlock)> Assignments =
            new List<(AssignmentExpressionSyntax, ExpressionStatementSyntax, BlockSyntax )>();

        public readonly List<(VariableDeclaratorSyntax varDeclarator, LocalDeclarationStatementSyntax containingStatement, SyntaxNode containingBlock)>
            DeclarationAndAssignments =
                new List<(VariableDeclaratorSyntax varDeclarator, LocalDeclarationStatementSyntax containingStatement, SyntaxNode containingBlock)>();


        public readonly List<ReturnStatementSyntax> ReturnsRecursive = new List<ReturnStatementSyntax>();

        public readonly List<ReturnStatementSyntax> Returns = new List<ReturnStatementSyntax>();
    }

    /// <summary>
    /// CollectInvocationsWalker is used by the <see cref="CalculatorMethodGenerator"/> to find all
    /// the places in a given function where a recursive call is being made to change that code in a
    /// stack-safe version of the function.
    /// </summary>
    internal class CollectInvocationsWalker : CSharpSyntaxWalker
    {
        private readonly GeneratorExecutionContext _context;
        private readonly SemanticModel _methodModel;
        private readonly TargetMethodsInfo _targetMethodsInfo;

        private readonly NodesToReplace _nodesToReplace = new NodesToReplace();

        private CollectInvocationsWalker(GeneratorExecutionContext context, SemanticModel methodModel, TargetMethodsInfo targetMethodsInfo)
        {
            _context = context;
            _methodModel = methodModel;
            _targetMethodsInfo = targetMethodsInfo;
        }

        internal static NodesToReplace CollectInvocations(GeneratorExecutionContext context, RecursiveMethodInfo methodInfo, TargetMethodsInfo targetMethodsInfo)
        {
            var walker = new CollectInvocationsWalker(context, methodInfo.MethodModel, targetMethodsInfo);
            walker.Visit(methodInfo.MethodSyntax.Body);
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
            var targetMethod = _targetMethodsInfo[invokeInfo.Symbol];
            if (targetMethod != null)
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
                case SyntaxKind.ExpressionStatement:
                {
                    // In a case of a call of a void method or a call such that return value is ignored
                    // the next parent seems to not matter at all.
                    var exprSyntax = (ExpressionStatementSyntax) parent;
                    _nodesToReplace.VoidCalls.Add((inv, exprSyntax));
                    return true;
                }

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
}