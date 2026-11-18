using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Elsa.CodeGeneration.Generators;

internal sealed partial class FixedProducerModel
{
    private bool CompositionType(ITypeSymbol? type) => type is INamedTypeSymbol named &&
        (named.ToDisplayString() == "Elsa.Features.Services.IModule" || _binding.IsFeature(named) ||
         named.ToDisplayString() == "Elsa.Features.Services.IFeature" ||
         named.AllInterfaces.Any(i => i.ToDisplayString() == "Elsa.Features.Services.IModule"));

    internal bool IsCompositionMethod(IMethodSymbol method)
    {
        // Binding an otherwise unconstrained T in Assert.That<T>(value) to IModule
        // does not turn the package method into an Elsa composition API.
        var declared = (method.ReducedFrom ?? method).OriginalDefinition;
        return CompositionType(declared.ContainingType) || CompositionType(declared.ContainingType.ExtensionParameter?.Type)
            || declared.Parameters.Any(p => CompositionType(p.Type));
    }

    private static bool IsRegistrationName(InvocationExpressionSyntax call) => (call.Expression switch
    {
        SimpleNameSyntax id => id,
        MemberAccessExpressionSyntax access => access.Name,
        MemberBindingExpressionSyntax binding => binding.Name,
        _ => null
    })?.Identifier.ValueText is "Configure" or "ConfigureHostedService" or "Use";

    private static (ITypeSymbol? Type, bool Hosted) Registration(IInvocationOperation op)
    {
        var method = op.TargetMethod.ReducedFrom ?? op.TargetMethod;
        if (method.ContainingType.ContainingAssembly.Name != "Elsa.Features") return default;
        var owner = method.ContainingType.ToDisplayString();
        var module = owner is "Elsa.Features.Services.IModule" or "Elsa.Features.Implementations.Module";
        var hosted = method.Name == "ConfigureHostedService" && (module || owner == "Elsa.Features.Abstractions.FeatureBase");
        if (!hosted && !(method.Name == "Configure" && module) &&
            !(method.Name == "Use" && owner == "Elsa.Extensions.DependencyInjectionExtensions")) return default;
        if (op.TargetMethod.TypeArguments.FirstOrDefault() is {} type) return (type, hosted);
        return ((op.Arguments.FirstOrDefault()?.Value as ITypeOfOperation)?.TypeOperand as INamedTypeSymbol, hosted);
    }

    /// <summary>Effects of executing a method body. Deferred delegates and uncalled local functions have no effects here.</summary>
    private sealed class ProducerEffects(FixedProducerModel model, CancellationToken token)
    {
        // Local functions need symbol identity: multiple local functions in one assembly
        // can all lack documentation IDs. Only exported, externally callable members use IDs.
        private readonly Dictionary<IMethodSymbol, FixedProducerSelector> _cache = new(SymbolEqualityComparer.Default);
        private readonly HashSet<IMethodSymbol> _reading = new(SymbolEqualityComparer.Default);
        public FixedProducerSelector Read(IMethodSymbol method)
        {
            var key = (method.ReducedFrom ?? method).OriginalDefinition;
            if (_cache.TryGetValue(key, out var cached)) return cached.Bind(method, allowOpen: true);
            if (!SymbolEqualityComparer.Default.Equals(method.ContainingAssembly, model._compilation.Assembly))
            {
                if (model.GetSelector(method, allowOpen: true) is {} imported) return imported;
                // Elsa's base lifecycle methods are empty. A third-party base can execute
                // arbitrary composition, even when a derived type declares no overrides.
                var emptyBase = method.ContainingAssembly.Name == "Elsa.Features"
                    && method.ContainingType.ToDisplayString() == "Elsa.Features.Abstractions.FeatureBase"
                    && (method.MethodKind == MethodKind.Constructor || method.Name is "Configure" or "ConfigureHostedServices" or "Apply" && method.Parameters.Length == 0);
                return new FixedProducerSelector { Error = emptyBase ? null : $"the package method '{method}' has no compiled feature-selection metadata" };
            }
            if (!_reading.Add(key))
                return new FixedProducerSelector { Error = $"recursive feature selection through '{method}'" };
            var summary = new Effects();
            if (method.MethodKind == MethodKind.Constructor)
                ReadInitialization(method, summary);
            foreach (var reference in method.OriginalDefinition.DeclaringSyntaxReferences)
            {
                var syntax = reference.GetSyntax(token);
                var semantic = model._compilation.GetSemanticModel(syntax.SyntaxTree);
                var body = syntax switch
                {
                    BaseMethodDeclarationSyntax m => (SyntaxNode?)m.Body ?? m.ExpressionBody?.Expression,
                    AccessorDeclarationSyntax accessor => (SyntaxNode?)accessor.Body ?? accessor.ExpressionBody?.Expression,
                    LocalFunctionStatementSyntax local => (SyntaxNode?)local.Body ?? local.ExpressionBody?.Expression,
                    _ => null
                };
                if (body is not null && semantic.GetOperation(body, token) is {} operation)
                    Visit(operation, summary, false);
            }
            _reading.Remove(key);
            var symbolic = summary.Finish();
            _cache[key] = symbolic;
            return symbolic.Bind(method, allowOpen: true);
        }

        private void ReadInitialization(IMethodSymbol ctor, Effects summary)
        {
            var initializers = new List<SyntaxNode>();
            foreach (var reference in ctor.OriginalDefinition.DeclaringSyntaxReferences)
            {
                if (reference.GetSyntax(token) is ConstructorDeclarationSyntax { Initializer: {} init }) initializers.Add(init);
                else if (reference.GetSyntax(token) is TypeDeclarationSyntax { BaseList: {} bases })
                    initializers.AddRange(bases.Types.OfType<PrimaryConstructorBaseTypeSyntax>());
            }
            if (initializers.Count == 0 && ctor.ContainingType.BaseType is {} parent)
            {
                var implicitBase = parent.InstanceConstructors.FirstOrDefault(m => m.Parameters.Length == 0);
                if (implicitBase is not null && model.CompositionType(parent)) summary.Add(Read(implicitBase), false);
            }
            foreach (var init in initializers)
            {
                var semantic = model._compilation.GetSemanticModel(init.SyntaxTree);
                if (semantic.GetSymbolInfo(init, token).Symbol is IMethodSymbol target && model.CompositionType(target.ContainingType))
                    summary.Add(Read(target), false);
                foreach (var arg in init.ChildNodes().OfType<ArgumentListSyntax>().SelectMany(a => a.Arguments))
                    if (semantic.GetOperation(arg.Expression, token) is {} operation) Visit(operation, summary, false);
            }
            // Field and auto-property initializers execute as part of construction, including
            // in primary- and implicit-constructor classes. Stored delegates remain deferred.
            foreach (var reference in ctor.ContainingType.OriginalDefinition.DeclaringSyntaxReferences)
            {
                if (reference.GetSyntax(token) is not TypeDeclarationSyntax declaration) continue;
                var semantic = model._compilation.GetSemanticModel(declaration.SyntaxTree);
                foreach (var member in declaration.Members)
                {
                    if (member is FieldDeclarationSyntax field && !field.Modifiers.Any(t => t.ValueText is "static" or "const"))
                        foreach (var variable in field.Declaration.Variables) VisitInitializer(variable.Initializer);
                    if (member is PropertyDeclarationSyntax property && !property.Modifiers.Any(t => t.ValueText == "static"))
                        VisitInitializer(property.Initializer);
                }

                void VisitInitializer(EqualsValueClauseSyntax? initializer)
                {
                    if (initializer != null && semantic.GetOperation(initializer.Value, token) is {} operation)
                        Visit(operation, summary, false);
                }
            }
        }

        public IEnumerable<FixedProducerSelector> ReadLifecycle(IMethodSymbol method, INamedTypeSymbol feature, HashSet<string> visiting, bool conditional = false)
        {
            var key = MethodKey(method);
            if (!visiting.Add(key)) yield break;
            var summary = Read(method);
            yield return conditional ? new FixedProducerSelector
            {
                ConditionalFeatures = Types(summary.Features.Concat(summary.ConditionalFeatures)), Hosted = summary.Hosted, Error = summary.Error
            } : summary;
            foreach (var target in summary.VirtualCalls.Concat(summary.ConditionalVirtualCalls))
            {
                var implementation = FindOverride(feature, target);
                if (implementation is null)
                    yield return new FixedProducerSelector { Error = $"cannot bind virtual feature callback '{target}' on '{feature}'" };
                else
                    foreach (var nested in ReadLifecycle(implementation, feature, visiting, conditional || summary.ConditionalVirtualCalls.Contains(target))) yield return nested;
            }
            visiting.Remove(key);
        }

        private static IMethodSymbol? FindOverride(INamedTypeSymbol feature, string target)
        {
            for (var type = feature; type is not null; type = type.BaseType)
                foreach (var member in type.GetMembers().OfType<IMethodSymbol>())
                    for (var parent = member; parent is not null; parent = parent.OverriddenMethod)
                        if (MethodKey(parent) == target) return member;
            return null;
        }

        private void Visit(IOperation op, Effects result, bool conditional)
        {
            token.ThrowIfCancellationRequested();
            switch (op)
            {
                case IBlockOperation block:
                    foreach (var statement in block.Operations)
                    {
                        Visit(statement, result, conditional);
                        if (statement is IReturnOperation or IThrowOperation) break;
                        if (CanReturn(statement)) conditional = true;
                    }
                    return;
                case IConditionalOperation branch:
                    Visit(branch.Condition, result, conditional);
                    if (branch.Condition.ConstantValue is { HasValue: true, Value: bool known })
                    {
                        if ((known ? branch.WhenTrue : branch.WhenFalse) is {} selected) Visit(selected, result, conditional);
                    }
                    else
                    {
                        Visit(branch.WhenTrue, result, true);
                        if (branch.WhenFalse is {} alternative) Visit(alternative, result, true);
                    }
                    return;
                // A non-null callback supplied by the host always runs at a null-conditional
                // invocation. The surrounding branch can still make that invocation conditional.
                case IConditionalAccessOperation { Operation: IParameterReferenceOperation parameter,
                    WhenNotNull: IInvocationOperation { TargetMethod.MethodKind: MethodKind.DelegateInvoke } invoke }
                    when IsConfigurationDelegate(parameter.Parameter.Type):
                    (conditional ? result.ConditionalCallbacks : result.Callbacks).Add(parameter.Parameter.Name);
                    foreach (var argument in invoke.Arguments) Visit(argument.Value, result, conditional);
                    return;
                case ILoopOperation or ISwitchOperation or ISwitchExpressionOperation or IConditionalAccessOperation or ICoalesceOperation or ICatchClauseOperation:
                    foreach (var child in op.ChildOperations) Visit(child, result, true);
                    return;
                case IBinaryOperation { OperatorKind: BinaryOperatorKind.ConditionalAnd or BinaryOperatorKind.ConditionalOr } binary:
                    Visit(binary.LeftOperand, result, conditional);
                    Visit(binary.RightOperand, result, true);
                    return;
                case IAnonymousFunctionOperation or ILocalFunctionOperation:
                    return;
                case IInvocationOperation call:
                    if (call.Instance != null) Visit(call.Instance, result, conditional);
                    foreach (var arg in call.Arguments) Visit(arg.Value, result, conditional);
                    VisitCall(call, result, conditional);
                    return;
                case ISimpleAssignmentOperation { Target: IPropertyReferenceOperation { Property.SetMethod: {} setter } }:
                    if (model.CompositionType(setter.ContainingType))
                        result.Add(Read(setter), conditional);
                    break;
            }
            foreach (var child in op.ChildOperations) Visit(child, result, conditional);
        }

        // A successful early return before a selector makes that selector conditional too.
        // Returns inside an uncalled local function or delegate do not exit the wrapper.
        private static bool CanReturn(IOperation op) => op switch
        {
            IReturnOperation => true,
            ILocalFunctionOperation or IAnonymousFunctionOperation => false,
            IConditionalOperation { Condition.ConstantValue: { HasValue: true, Value: bool choice } } branch =>
                (choice ? branch.WhenTrue : branch.WhenFalse) is {} selected && CanReturn(selected),
            _ => op.ChildOperations.Any(CanReturn)
        };

        private void VisitCall(IInvocationOperation call, Effects result, bool conditional)
        {
            var method = call.TargetMethod;
            var (type, hosted) = Registration(call);
            if (hosted || type is not null)
            {
                var term = type switch
                {
                    INamedTypeSymbol closed when FeatureBinding.IsClosed(closed) => new SelectorType(closed),
                    ITypeParameterSymbol { TypeParameterKind: TypeParameterKind.Method } parameter => new SelectorType(parameter),
                    _ => (SelectorType?)null
                };
                if (!term.HasValue)
                    result.Error ??= $"the selected type in '{method}' is not statically known";
                else if (hosted)
                    result.Hosted.Add(term.Value);
                else
                {
                    result.AddType(term.Value, conditional);
                    if (!conditional && call.Arguments.Any(argument => argument.Parameter?.Name == "factory"))
                        result.FactoryFeatures.Add(term.Value);
                    foreach (var arg in call.Arguments.Where(a => a.Parameter?.Type is INamedTypeSymbol { Name: "Action" }))
                        Callback(arg.Value, result, conditional);
                }
                return;
            }
            if (method.MethodKind == MethodKind.DelegateInvoke)
            {
                if (call.Instance is IParameterReferenceOperation parameter && IsConfigurationDelegate(parameter.Parameter.Type))
                    (conditional ? result.ConditionalCallbacks : result.Callbacks).Add(parameter.Parameter.Name);
                return;
            }
            var fullMethod = method.ReducedFrom ?? method;
            if (fullMethod.ContainingType.ContainingAssembly.Name == "Elsa.Features" &&
                fullMethod.ContainingType.ToDisplayString() is "Elsa.Features.Services.IModule" or "Elsa.Features.Abstractions.FeatureBase")
                return;
            if (!model.IsCompositionMethod(fullMethod) && method.MethodKind != MethodKind.LocalFunction)
                return;
            if (fullMethod.Name == "Use" && fullMethod.ContainingType.ToDisplayString() == "Elsa.Extensions.DependencyInjectionExtensions"
                && fullMethod.ContainingAssembly.Name == "Elsa.Features")
                return;
            if (call.IsVirtual && call.Instance is IInstanceReferenceOperation && model.CompositionType(method.ContainingType))
            {
                (conditional ? result.ConditionalVirtualCalls : result.VirtualCalls).Add(MethodKey(method));
                return;
            }
            var local = SymbolEqualityComparer.Default.Equals(method.ContainingAssembly, model._compilation.Assembly);
            var effects = local ? Read(method) : model.GetSelector(method, allowOpen: true);
            if (effects is null)
            {
                result.Error ??= $"the package method '{method}' has no compiled feature-selection metadata";
                return;
            }
            result.Add(effects, conditional);
            if (method.MethodKind == MethodKind.LocalFunction)
            {
                // A local function can forward an enclosing callback without taking it as
                // an argument. Its own parameters are mapped through the call below.
                var parameters = method.Parameters.Select(p => p.Name).ToArray();
                (conditional ? result.ConditionalCallbacks : result.Callbacks).UnionWith(effects.ConfigurationParameters.Except(parameters));
                result.ConditionalCallbacks.UnionWith(effects.ConditionalConfigurationParameters.Except(parameters));
            }
            foreach (var arg in call.Arguments)
            {
                if (effects.ConfigurationParameters.Contains(arg.Parameter?.Name)) Callback(arg.Value, result, conditional);
                else if (effects.ConditionalConfigurationParameters.Contains(arg.Parameter?.Name)) Callback(arg.Value, result, true);
            }
        }

        private bool IsConfigurationDelegate(ITypeSymbol type) => type is INamedTypeSymbol { DelegateInvokeMethod: {} invoke } &&
            invoke.Parameters.Any(p => model.CompositionType(p.Type));

        private void Callback(IOperation op, Effects result, bool conditional)
        {
            switch (op)
            {
                case IDelegateCreationOperation creation: Callback(creation.Target, result, conditional); break;
                case IConversionOperation conversion: Callback(conversion.Operand, result, conditional); break;
                case IAnonymousFunctionOperation lambda: Visit(lambda.Body, result, conditional); break;
                case IMethodReferenceOperation group: result.Add(Read(group.Method), conditional); break;
                case IParameterReferenceOperation parameter: (conditional ? result.ConditionalCallbacks : result.Callbacks).Add(parameter.Parameter.Name); break;
                default:
                    if (op.ConstantValue is not { HasValue: true, Value: null })
                        result.Error ??= $"cannot inspect feature configuration callback '{op.Syntax}'";
                    break;
            }
        }

        private sealed class Effects
        {
            public readonly List<SelectorType> Features = [];
            public readonly List<SelectorType> FactoryFeatures = [];
            public readonly List<SelectorType> Hosted = [];
            public readonly List<SelectorType> ConditionalFeatures = [];
            public readonly HashSet<string> Callbacks = new(StringComparer.Ordinal);
            public readonly HashSet<string> ConditionalCallbacks = new(StringComparer.Ordinal);
            public readonly HashSet<string> VirtualCalls = new(StringComparer.Ordinal);
            public readonly HashSet<string> ConditionalVirtualCalls = new(StringComparer.Ordinal);
            public string? Error;
            public void AddType(SelectorType type, bool conditional)
            {
                (conditional ? ConditionalFeatures : Features).Add(type);
            }
            public void Add(FixedProducerSelector selector, bool conditional)
            {
                Error ??= selector.Error;
                foreach (var feature in selector.SelectedTypes) AddType(feature, conditional);
                if (!conditional) FactoryFeatures.AddRange(selector.FactoryTypes);
                Hosted.AddRange(selector.HostedTypes);
                ConditionalFeatures.AddRange(selector.ConditionalTypes);
                ConditionalVirtualCalls.UnionWith(selector.ConditionalVirtualCalls);
                (conditional ? ConditionalVirtualCalls : VirtualCalls).UnionWith(selector.VirtualCalls);
            }
            public FixedProducerSelector Finish() => new()
            {
                SelectedTypes = Features.ToArray(), ConditionalTypes = ConditionalFeatures.ToArray(), HostedTypes = Hosted.ToArray(), Error = Error,
                FactoryTypes = FactoryFeatures.ToArray(),
                ConfigurationParameters = Callbacks.OrderBy(s => s, StringComparer.Ordinal).ToArray(),
                ConditionalConfigurationParameters = ConditionalCallbacks.Except(Callbacks).OrderBy(s => s, StringComparer.Ordinal).ToArray(),
                VirtualCalls = VirtualCalls.OrderBy(s => s, StringComparer.Ordinal).ToArray(),
                ConditionalVirtualCalls = ConditionalVirtualCalls.OrderBy(s => s, StringComparer.Ordinal).ToArray()
            };
        }
    }
}
