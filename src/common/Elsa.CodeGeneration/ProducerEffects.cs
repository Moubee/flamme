using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Elsa.CodeGeneration.Generators;

internal sealed partial class FixedProducerModel
{
    private static bool IsCompositionContract(ITypeSymbol type) => type.ContainingAssembly?.Name == "Elsa.Features" &&
        type.ToDisplayString() is "Elsa.Features.Services.IModule" or "Elsa.Features.Services.IFeature";

    internal bool CompositionType(ITypeSymbol? type, HashSet<ITypeParameterSymbol>? seen = null)
    {
        if (type is not ITypeParameterSymbol parameter)
            return type != null && (IsCompositionContract(type) || type.AllInterfaces.Any(IsCompositionContract));
        seen ??= new HashSet<ITypeParameterSymbol>(SymbolEqualityComparer.Default);
        return seen.Add(parameter) && parameter.ConstraintTypes.Any(constraint => CompositionType(constraint, seen));
    }

    internal bool IsCompositionMethod(IMethodSymbol method)
    {
        // Binding an otherwise unconstrained T in Assert.That<T>(value) to IModule
        // does not turn the package method into an Elsa composition API.
        var declared = (method.ReducedFrom ?? method).OriginalDefinition;
        return CompositionType(declared.ContainingType) || CompositionType(declared.ContainingType.ExtensionParameter?.Type)
            || declared.Parameters.Any(p => CompositionType(p.Type));
    }

    internal static bool IsFrameworkProperty(IPropertySymbol property) => property.ContainingAssembly.Name == "Elsa.Features" &&
        property.ContainingType.ToDisplayString() is "Elsa.Features.Abstractions.FeatureBase" or "Elsa.Features.Services.IFeature"
            or "Elsa.Features.Services.IModule" or "Elsa.Features.Implementations.Module" or "Elsa.Features.Services.FixedFeatureModule";

    private static bool IsAppConfigurator(IPropertySymbol property) => property.Name == "Configurator" &&
        property.ContainingAssembly.Name == "Elsa" && property.ContainingType.ToDisplayString() == "Elsa.Features.AppFeature";

    internal static bool CallsVirtualGetter(IPropertyReferenceOperation property, IMethodSymbol method) =>
        method.MethodKind == MethodKind.PropertyGet && (method.IsVirtual || method.IsOverride || method.IsAbstract) && !method.IsSealed && !method.ContainingType.IsSealed &&
        property.Syntax is not MemberAccessExpressionSyntax { Expression: BaseExpressionSyntax };

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
            if (key is { MethodKind: MethodKind.PropertySet, AssociatedSymbol: IPropertySymbol assigned } && IsAppConfigurator(assigned))
                return new FixedProducerSelector { Error = "assign the host configurator through AddElsa or ConfigureElsa so its feature selections can be planned" };
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
            var implementation = key.PartialImplementationPart ?? key;
            foreach (var reference in implementation.DeclaringSyntaxReferences)
            {
                var syntax = reference.GetSyntax(token);
                var semantic = model._compilation.GetSemanticModel(syntax.SyntaxTree);
                var body = syntax switch
                {
                    BaseMethodDeclarationSyntax m => (SyntaxNode?)m.Body ?? m.ExpressionBody?.Expression,
                    AccessorDeclarationSyntax accessor => (SyntaxNode?)accessor.Body ?? accessor.ExpressionBody?.Expression,
                    ArrowExpressionClauseSyntax arrow => arrow.Expression,
                    PropertyDeclarationSyntax property => property.ExpressionBody?.Expression,
                    IndexerDeclarationSyntax indexer => indexer.ExpressionBody?.Expression,
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
                foreach (var arg in init.ChildNodes().OfType<ArgumentListSyntax>().SelectMany(a => a.Arguments))
                    if (semantic.GetOperation(arg.Expression, token) is {} operation) Visit(operation, summary, false);
                if (semantic.GetSymbolInfo(init, token).Symbol is IMethodSymbol target && model.IsCompositionMethod(target))
                    summary.Add(Read(target), false);
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
            foreach (var (call, guarded) in summary.VirtualCalls.Select(call => (call, conditional))
                         .Concat(summary.ConditionalVirtualCalls.Select(call => (call, true))))
            {
                var implementation = FindOverride(feature, call.MethodKey);
                if (implementation == null || implementation.Arity != call.Arguments.Length || call.Arguments.Any(argument => argument.Concrete == null))
                    yield return new FixedProducerSelector { Error = $"cannot bind virtual feature callback '{call.MethodKey}' on '{feature}'" };
                else
                {
                    if (implementation.IsGenericMethod)
                        implementation = implementation.Construct(call.Arguments.Select(argument => (ITypeSymbol)argument.Concrete!).ToArray());
                    foreach (var nested in ReadLifecycle(implementation, feature, visiting, guarded)) yield return nested;
                }
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
                case IConditionalAccessOperation { Operation: {} receiver,
                    WhenNotNull: IInvocationOperation { TargetMethod.MethodKind: MethodKind.DelegateInvoke } invoke }
                    when receiver.Type is {} delegateType && IsConfigurationDelegate(delegateType):
                    foreach (var argument in invoke.Arguments) Visit(argument.Value, result, conditional);
                    Callback(receiver, result, conditional);
                    return;
                case IConditionalAccessOperation access:
                    Visit(access.Operation, result, conditional);
                    Visit(access.WhenNotNull, result, true);
                    return;
                case ICoalesceOperation coalesce:
                    Visit(coalesce.Value, result, conditional);
                    Visit(coalesce.WhenNull, result, true);
                    return;
                case ILoopOperation or ISwitchOperation or ISwitchExpressionOperation or ICatchClauseOperation:
                    foreach (var child in op.ChildOperations) Visit(child, result, true);
                    return;
                case IBinaryOperation { OperatorKind: BinaryOperatorKind.ConditionalAnd or BinaryOperatorKind.ConditionalOr } binary:
                    Visit(binary.LeftOperand, result, conditional);
                    Visit(binary.RightOperand, result, true);
                    return;
                case IAnonymousFunctionOperation or ILocalFunctionOperation or INameOfOperation:
                    return;
                case IInvocationOperation call:
                    if (call.Instance != null) Visit(call.Instance, result, conditional);
                    foreach (var arg in call.Arguments) Visit(arg.Value, result, conditional);
                    VisitCall(call, result, conditional);
                    return;
                case IAssignmentOperation { Target: IPropertyReferenceOperation property } assignment:
                    VisitProperty(property, result, conditional, read: assignment is not ISimpleAssignmentOperation || property.Property.ReturnsByRef);
                    var conditionalWrite = conditional || assignment is ICoalesceAssignmentOperation;
                    Visit(assignment.Value, result, conditionalWrite);
                    VisitAccessor(property, property.Property.SetMethod, result, conditionalWrite);
                    return;
                case ICoalesceAssignmentOperation assignment:
                    Visit(assignment.Target, result, conditional);
                    Visit(assignment.Value, result, true);
                    return;
                case IIncrementOrDecrementOperation { Target: IPropertyReferenceOperation property }:
                    VisitProperty(property, result, conditional, read: true);
                    VisitAccessor(property, property.Property.SetMethod, result, conditional);
                    return;
                case IPropertyReferenceOperation property:
                    VisitProperty(property, result, conditional, read: true);
                    return;
            }
            foreach (var child in op.ChildOperations) Visit(child, result, conditional);
        }

        private void VisitProperty(IPropertyReferenceOperation property, Effects result, bool conditional, bool read)
        {
            foreach (var child in property.ChildOperations) Visit(child, result, conditional);
            if (read) VisitAccessor(property, property.Property.GetMethod, result, conditional);
        }

        private void VisitAccessor(IPropertyReferenceOperation property, IMethodSymbol? method, Effects result, bool conditional)
        {
            if (method == null || IsFrameworkProperty(property.Property) || !model.IsCompositionMethod(method)) return;
            if (!CallsVirtualGetter(property, method))
                result.Add(Read(method), conditional);
            else if (property.Instance is IInstanceReferenceOperation)
                result.AddVirtualCall(method, conditional);
            else if (property.Instance?.Type is INamedTypeSymbol { IsSealed: true } receiver && FindOverride(receiver, MethodKey(method)) is { } implementation)
                result.Add(Read(implementation), conditional);
            else
                result.Error ??= $"cannot bind virtual feature accessor '{method}' to a concrete receiver";
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
                var term = SelectorType.From(type);
                if (!term.HasValue)
                    result.Error ??= $"the selected type in '{method}' is not statically known";
                else if (hosted)
                    result.Hosted.Add(term.Value);
                else
                {
                    result.AddResolution(new SlotResolution(term.Value, conditional,
                        call.Arguments.Any(argument => argument.Parameter?.Name == "factory")));
                    foreach (var arg in call.Arguments.Where(a => a.Parameter?.Type is INamedTypeSymbol { Name: "Action" }))
                        Callback(arg.Value, result, conditional);
                }
                return;
            }
            if (method.MethodKind == MethodKind.DelegateInvoke)
            {
                if (call.Instance?.Type is {} delegateType && IsConfigurationDelegate(delegateType))
                    Callback(call.Instance, result, conditional);
                return;
            }
            var fullMethod = method.ReducedFrom ?? method;
            if (fullMethod.Name == "Apply" && fullMethod.ContainingType.ToDisplayString() == "Elsa.Features.Services.IModule" &&
                fullMethod.ContainingAssembly.Name == "Elsa.Features")
                result.AddResolution(new SlotResolution(null, conditional));
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
                result.AddVirtualCall(method, conditional);
                return;
            }
            var local = SymbolEqualityComparer.Default.Equals(method.ContainingAssembly, model._compilation.Assembly);
            var effects = local ? Read(method) : model.GetSelector(method, allowOpen: true);
            if (effects is null)
            {
                result.Error ??= $"the package method '{method}' has no compiled feature-selection metadata";
                return;
            }
            result.Add(effects, conditional, (name, guarded) =>
            {
                var argument = call.Arguments.FirstOrDefault(arg => arg.Parameter?.Name == name);
                if (argument != null) Callback(argument.Value, result, guarded);
                // A local function can invoke a callback captured from its enclosing method.
                else if (method.MethodKind == MethodKind.LocalFunction)
                    result.AddResolution(new SlotResolution(null, guarded, callback: name));
            });
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
                case ILocalReferenceOperation local:
                    if (CallbackAlias(local) is {} value)
                        Callback(value, result, conditional);
                    else
                        result.Error ??= $"cannot establish which feature configuration callback '{local.Local.Name}' invokes";
                    break;
                // Generated SDK calls queue their known callbacks on FixedFeatureModule.
                // AppFeature's managed callback stays null, and direct writes are diagnosed.
                case IPropertyReferenceOperation property when IsAppConfigurator(property.Property): break;
                case IMethodReferenceOperation group: result.Add(Read(group.Method), conditional); break;
                case IParameterReferenceOperation parameter:
                    result.AddResolution(new SlotResolution(null, conditional, callback: parameter.Parameter.Name));
                    break;
                default:
                    if (op.ConstantValue is not { HasValue: true, Value: null })
                        result.Error ??= $"cannot inspect feature configuration callback '{op.Syntax}'";
                    break;
            }
        }

        private IOperation? CallbackAlias(ILocalReferenceOperation local)
        {
            if (local.Local.RefKind != RefKind.None || local.Local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(token) is not VariableDeclaratorSyntax declaration)
                return null;
            var semantic = model._compilation.GetSemanticModel(declaration.SyntaxTree);
            var writes = HostSelections.FindWrites(local.Local, semantic).ToArray();
            // A captured write or read can execute at a different time from its source position.
            var function = EnclosingFunction(local.Syntax);
            if (writes.Any(write => EnclosingFunction(write.Site) != function)) return null;
            var write = writes.LastOrDefault(candidate => candidate.Site.Span.End <= local.Syntax.SpanStart);
            if (write != null && (write.Value == null || HostSelections.AmbiguousWrite(write.Site, local.Syntax, semantic))) return null;
            var value = write?.Value ?? declaration.Initializer?.Value;
            return value != null && value.Span.End <= local.Syntax.SpanStart ? HostSelections.ExpressionOperation(semantic, value) : null;

            static SyntaxNode? EnclosingFunction(SyntaxNode node) => node.Ancestors()
                .FirstOrDefault(parent => parent is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax or BaseMethodDeclarationSyntax);
        }

        private sealed class Effects
        {
            public readonly List<SelectorType> Features = [];
            private readonly List<SlotResolution> _resolutions = [];
            public readonly List<SelectorType> Hosted = [];
            public readonly List<SelectorType> ConditionalFeatures = [];
            public readonly HashSet<string> Callbacks = new(StringComparer.Ordinal);
            public readonly HashSet<string> ConditionalCallbacks = new(StringComparer.Ordinal);
            public readonly List<VirtualCall> VirtualCalls = [];
            public readonly List<VirtualCall> ConditionalVirtualCalls = [];

            public void AddVirtualCall(IMethodSymbol method, bool conditional)
            {
                var arguments = method.TypeArguments.Select(SelectorType.From).ToArray();
                if (arguments.Any(argument => !argument.HasValue))
                    Error ??= $"the type arguments for virtual feature callback '{method}' are not statically known";
                else
                    (conditional ? ConditionalVirtualCalls : VirtualCalls).Add(new VirtualCall(MethodKey(method), arguments.Select(argument => argument!.Value).ToArray()));
            }
            public string? Error;
            public void AddResolution(SlotResolution step)
            {
                _resolutions.Add(step);
                if (step.Type is { } type)
                    (step.Conditional ? ConditionalFeatures : Features).Add(type);
                if (step.Callback != null)
                    (step.Conditional ? ConditionalCallbacks : Callbacks).Add(step.Callback);
            }
            public void Add(FixedProducerSelector selector, bool conditional, Action<string, bool>? invoke = null)
            {
                Error ??= selector.Error;
                foreach (var step in selector.OrderedResolutions())
                {
                    var guarded = conditional || step.Conditional;
                    if (step.Callback != null)
                        invoke?.Invoke(step.Callback, guarded);
                    else
                        AddResolution(new SlotResolution(step.Type, guarded, step.Factory));
                }
                Hosted.AddRange(selector.HostedTypes);
                ConditionalVirtualCalls.AddRange(selector.ConditionalVirtualCalls);
                (conditional ? ConditionalVirtualCalls : VirtualCalls).AddRange(selector.VirtualCalls);
            }
            public FixedProducerSelector Finish() => new()
            {
                SelectedTypes = Features.ToArray(), ConditionalTypes = ConditionalFeatures.ToArray(), HostedTypes = Hosted.ToArray(), Error = Error,
                Resolutions = _resolutions.Any(step => step.Factory || step.Type == null) ? _resolutions.ToArray() : [],
                ConfigurationParameters = Callbacks.OrderBy(s => s, StringComparer.Ordinal).ToArray(),
                ConditionalConfigurationParameters = ConditionalCallbacks.Except(Callbacks).OrderBy(s => s, StringComparer.Ordinal).ToArray(),
                VirtualCalls = VirtualCalls.ToArray(),
                ConditionalVirtualCalls = ConditionalVirtualCalls.ToArray()
            };
        }
    }
}
