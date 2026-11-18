using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Elsa.CodeGeneration.Generators;

internal sealed partial class FixedProducerModel
{
    /// <summary>Records which input owns a helper's returned module or feature, independently of its selection effects.</summary>
    internal sealed class FeatureReturnOwners(FixedProducerModel model)
    {
        private const string Instance = "$this";
        private readonly Dictionary<IMethodSymbol, string?> _owners = new(SymbolEqualityComparer.Default);

        internal IOperation? ReturnedInput(IInvocationOperation call)
        {
            var owner = Read(call.TargetMethod);
            if (owner == null) return null;
            if (owner == Instance) return call.Instance;
            if (call.TargetMethod.ReducedFrom is { } extension && extension.Parameters[0].Name == owner)
                return call.Instance;
            return call.Arguments.FirstOrDefault(argument => argument.Parameter?.Name == owner)?.Value;
        }

        internal IOperation? ReturnedInput(IPropertyReferenceOperation property)
        {
            if (property.Property.GetMethod is not { } getter) return null;
            if (property.Property.Name == "Module" && IsFrameworkProperty(property.Property))
            {
                if (getter.ContainingType.ToDisplayString() == "Elsa.Features.Abstractions.FeatureBase") return property.Instance;
                // An explicit IFeature.Module implementation can return a different module.
                var receiver = property.Instance;
                while (receiver is IConversionOperation conversion && (conversion.Conversion.IsIdentity || conversion.Conversion.IsReference))
                    receiver = conversion.Operand;
                if (receiver?.Type is not INamedTypeSymbol { IsSealed: true } concrete || BindVirtualMethod(getter, concrete) is not {} implementation)
                    return null;
                if (implementation.ContainingType.ToDisplayString() == "Elsa.Features.Abstractions.FeatureBase") return receiver;
                getter = implementation;
            }
            var owner = Read(getter);
            if (owner == null) return null;
            return owner == Instance ? property.Instance : property.Arguments.FirstOrDefault(argument => argument.Parameter?.Name == owner)?.Value;
        }

        internal string? Read(IMethodSymbol method)
        {
            method = (method.ReducedFrom ?? method).OriginalDefinition;
            method = method.PartialDefinitionPart ?? method;
            if (method.ContainingAssembly.Name == "Elsa.Features")
            {
                var type = method.ContainingType.ToDisplayString();
                if (type == "Elsa.Features.Services.IModule" && method.Name is "Configure" or "ConfigureHostedService")
                    return Instance;
                if (type == "Elsa.Extensions.DependencyInjectionExtensions" && method.Name == "Use")
                    return method.Parameters[0].Name;
            }
            if (!SymbolEqualityComparer.Default.Equals(method.ContainingAssembly, model._compilation.Assembly))
                return model.GetSelector(method, allowOpen: true)?.ReturnOwner;
            if (_owners.TryGetValue(method, out var known)) return known;
            _owners[method] = null; // Recursive helpers cannot establish a return owner by referring to themselves.
            if (method.IsAbstract || (method.IsVirtual || method.IsOverride) && !method.IsSealed && !method.ContainingType.IsSealed)
                return null;
            if (!model.CompositionType(method.ReturnType) && method.ReturnType.TypeKind != TypeKind.TypeParameter)
                return null;

            var declaration = (method.PartialImplementationPart ?? method).DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
            var body = declaration switch
            {
                MethodDeclarationSyntax ordinary => (SyntaxNode?)ordinary.ExpressionBody?.Expression ?? ordinary.Body,
                LocalFunctionStatementSyntax local => (SyntaxNode?)local.ExpressionBody?.Expression ?? local.Body,
                AccessorDeclarationSyntax accessor => (SyntaxNode?)accessor.ExpressionBody?.Expression ?? accessor.Body,
                ArrowExpressionClauseSyntax arrow => arrow.Expression,
                PropertyDeclarationSyntax property => property.ExpressionBody?.Expression,
                IndexerDeclarationSyntax indexer => indexer.ExpressionBody?.Expression,
                _ => null
            };
            if (body == null) return null;
            var semantic = model._compilation.GetSemanticModel(body.SyntaxTree);
            var declared = semantic.GetDeclaredSymbol(declaration!) as IMethodSymbol ?? method;
            var expressions = body is ExpressionSyntax expression ? [expression] :
                body.DescendantNodes(node => node is not AnonymousFunctionExpressionSyntax and not LocalFunctionStatementSyntax)
                    .OfType<ReturnStatementSyntax>().Select(statement => statement.Expression).ToArray();
            if (expressions.Length == 0) return null;
            var modified = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            foreach (var node in body.DescendantNodesAndSelf())
            {
                switch (node)
                {
                    case AssignmentExpressionSyntax assignment: MarkWritten(assignment.Left); break;
                    case ArgumentSyntax argument when argument.RefKindKeyword.ValueText is "ref" or "out": MarkWritten(argument.Expression); break;
                    case RefExpressionSyntax reference: MarkWritten(reference.Expression); break;
                }
            }
            // Writes through one by-reference parameter can change another parameter bound to the same storage.
            var writesThroughReference = modified.OfType<IParameterSymbol>().Any(parameter => parameter.RefKind is RefKind.Ref or RefKind.Out);
            string? result = null;
            foreach (var returned in expressions)
            {
                var owner = returned == null ? null : Origin(HostSelections.ExpressionOperation(semantic, returned), new HashSet<ISymbol>(SymbolEqualityComparer.Default));
                if (owner == null || result != null && owner != result) return null;
                result = owner;
            }
            return _owners[method] = result;

            void MarkWritten(ExpressionSyntax target)
            {
                if (target is TupleExpressionSyntax tuple)
                    foreach (var argument in tuple.Arguments) MarkWritten(argument.Expression);
                else if (target is ParenthesizedExpressionSyntax parens)
                    MarkWritten(parens.Expression);
                else if (target is IdentifierNameSyntax name && semantic.GetSymbolInfo(name).Symbol is { } symbol)
                    modified.Add(symbol);
            }

            string? Origin(IOperation? value, HashSet<ISymbol> seen)
            {
                switch (value)
                {
                    case IParameterReferenceOperation parameter when !modified.Contains(parameter.Parameter) && (parameter.Parameter.RefKind == RefKind.None || !writesThroughReference):
                        if (SymbolEqualityComparer.Default.Equals(parameter.Parameter, declared.ContainingType.ExtensionParameter))
                            return Instance;
                        return SymbolEqualityComparer.Default.Equals(parameter.Parameter.ContainingSymbol, declared) ? method.Parameters[parameter.Parameter.Ordinal].Name : null;
                    case IInstanceReferenceOperation:
                        return Instance;
                    case ILocalReferenceOperation local when !modified.Contains(local.Local) && seen.Add(local.Local):
                        var initializer = (local.Local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() as VariableDeclaratorSyntax)?.Initializer?.Value;
                        return initializer == null ? null : Origin(HostSelections.ExpressionOperation(semantic, initializer), seen);
                    case IConversionOperation conversion when conversion.OperatorMethod == null:
                        return Origin(conversion.Operand, seen);
                    case IParenthesizedOperation parentheses:
                        return Origin(parentheses.Operand, seen);
                    case IInvocationOperation call:
                        return Origin(ReturnedInput(call), seen);
                    case IPropertyReferenceOperation property:
                        return Origin(ReturnedInput(property), seen);
                    case IConditionalOperation conditional:
                        if (conditional.Condition.ConstantValue is { HasValue: true, Value: bool choice })
                            return Origin(choice ? conditional.WhenTrue : conditional.WhenFalse, seen);
                        var first = Origin(conditional.WhenTrue, new HashSet<ISymbol>(seen, SymbolEqualityComparer.Default));
                        return first != null && first == Origin(conditional.WhenFalse, seen) ? first : null;
                    default:
                        return null;
                }
            }
        }
    }
}
