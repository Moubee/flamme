using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Elsa.CodeGeneration.Generators;

internal static class DelegateTargets
{
    internal static IOperation? Resolve(IOperation? target, Compilation compilation, CancellationToken token, SyntaxNode? readSite = null)
    {
        while (target != null)
        {
            switch (target)
            {
                case IDelegateCreationOperation creation: target = creation.Target; break;
                case IConversionOperation { OperatorMethod: null } conversion: target = conversion.Operand; break;
                case IParenthesizedOperation parentheses: target = parentheses.Operand; break;
                case ILocalReferenceOperation local: target = LocalValue(local, compilation, token, readSite); readSite = null; break;
                default: return target;
            }
        }
        return null;
    }

    internal static SyntaxNode CapturedRead(ILocalReferenceOperation local, IEnumerable<(SyntaxNode Function, SyntaxNode Call)> calls)
    {
        var read = local.Syntax;
        var declaration = local.Local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
        foreach (var (function, call) in calls)
            if (function.SyntaxTree == read.SyntaxTree && function.Span.Contains(read.Span) &&
                (declaration?.SyntaxTree != function.SyntaxTree || !function.Span.Contains(declaration.Span)))
                read = call;
        return read;
    }

    internal static IOperation? LocalValue(ILocalReferenceOperation local, Compilation compilation, CancellationToken token, SyntaxNode? readSite = null)
    {
        if (local.Local.RefKind != RefKind.None || local.Local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(token) is not VariableDeclaratorSyntax declaration)
            return null;
        var semantic = compilation.GetSemanticModel(declaration.SyntaxTree);
        var read = readSite ?? local.Syntax;
        if (read.SyntaxTree != declaration.SyntaxTree) return null;
        var writes = HostSelections.FindWrites(local.Local, semantic).ToArray();
        var function = EnclosingFunction(read);
        if (writes.Any(write => EnclosingFunction(write.Site) != function)) return null;
        var write = writes.LastOrDefault(candidate => candidate.Site.Span.End <= read.SpanStart);
        if (write != null && (write.Value == null || HostSelections.AmbiguousWrite(write.Site, read, semantic))) return null;
        var value = write?.Value ?? declaration.Initializer?.Value;
        return value != null && value.Span.End <= read.SpanStart ? HostSelections.ExpressionOperation(semantic, value) : null;
    }

    internal static IEnumerable<IOperation> PossibleValues(ILocalReferenceOperation local, Compilation compilation, SyntaxNode read)
    {
        if (local.Local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is not VariableDeclaratorSyntax declaration) yield break;
        var semantic = compilation.GetSemanticModel(declaration.SyntaxTree);
        var writes = HostSelections.FindWrites(local.Local, semantic).Where(write =>
            write.Site.Span.End <= read.SpanStart || EnclosingFunction(write.Site) != EnclosingFunction(read));
        foreach (var expression in writes.Select(write => write.Value ?? (write.Site as AssignmentExpressionSyntax)?.Right).Prepend(declaration.Initializer?.Value))
            if (expression != null && HostSelections.ExpressionOperation(semantic, expression) is {} value)
                yield return value;
    }

    internal static IEnumerable<(IOperation Target, bool Conditional)> Choices(IConditionalOperation branch)
    {
        if (branch.Condition.ConstantValue is { HasValue: true, Value: bool known })
        {
            if ((known ? branch.WhenTrue : branch.WhenFalse) is {} selected) yield return (selected, false);
        }
        else
        {
            yield return (branch.WhenTrue, true);
            if (branch.WhenFalse != null) yield return (branch.WhenFalse, true);
        }
    }

    private static SyntaxNode? EnclosingFunction(SyntaxNode node) => node.Ancestors()
        .FirstOrDefault(parent => parent is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax or BaseMethodDeclarationSyntax);
}
