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

    internal static bool RequiresVirtualDispatch(IMethodSymbol method) => !method.IsStatic && !method.IsSealed && !method.ContainingType.IsSealed &&
        (method.IsVirtual || method.IsOverride || method.IsAbstract || method.ContainingType.TypeKind == TypeKind.Interface);

    internal static bool CallsVirtualAccessor(IPropertyReferenceOperation property, IMethodSymbol method) =>
        RequiresVirtualDispatch(method) &&
        property.Syntax is not MemberAccessExpressionSyntax { Expression: BaseExpressionSyntax } and not ElementAccessExpressionSyntax { Expression: BaseExpressionSyntax };

    internal static IMethodSymbol? BindVirtualMethod(IMethodSymbol method, INamedTypeSymbol receiver)
    {
        var implementation = FindOverride(receiver, MethodKey(method));
        return implementation is { IsGenericMethod: true } ? implementation.ConstructedFrom.Construct(method.TypeArguments.ToArray()) : implementation;
    }

    internal static IMethodSymbol? FindOverride(INamedTypeSymbol receiver, string target)
    {
        for (var type = receiver; type != null; type = type.BaseType)
            foreach (var member in type.GetMembers().OfType<IMethodSymbol>())
                for (var parent = member; parent != null; parent = parent.OverriddenMethod)
                    if (MethodKey(parent) == target) return member;

        foreach (var contract in receiver.AllInterfaces)
            foreach (var member in contract.GetMembers().OfType<IMethodSymbol>())
                if (MethodKey(member) == target && receiver.FindImplementationForInterfaceMember(member) is IMethodSymbol implementation)
                    return implementation.ContainingType.TypeKind == TypeKind.Interface ? implementation : FindOverride(receiver, MethodKey(implementation));
        return null;
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
        private readonly HashSet<SyntaxNode> _invoking = [];
        private readonly Stack<(SyntaxNode Function, SyntaxNode Call)> _delegateCalls = new();
        private readonly FeatureReturnOwners _returnOwners = new(model);
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
            var summary = new Effects(key);
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
            // Only the constructor that calls base runs the instance initializers. They
            // execute before the base arguments and constructor, even for a primary constructor.
            if (!initializers.Any(init => init is ConstructorInitializerSyntax { ThisOrBaseKeyword.ValueText: "this" }))
                ReadMemberInitializers(ctor, summary);
            if (initializers.Count == 0 && ctor.ContainingType.BaseType is {} parent)
            {
                var implicitBase = parent.InstanceConstructors.FirstOrDefault(m => m.Parameters.Length == 0);
                if (implicitBase != null && model.CompositionType(parent)) summary.Add(Read(implicitBase), false);
            }
            foreach (var init in initializers)
            {
                var semantic = model._compilation.GetSemanticModel(init.SyntaxTree);
                var arguments = init.ChildNodes().OfType<ArgumentListSyntax>().SelectMany(list => list.Arguments)
                    .Select(arg => semantic.GetOperation(arg, token)).OfType<IArgumentOperation>().ToArray();
                foreach (var arg in arguments) Visit(arg.Value, summary, false);
                if (semantic.GetSymbolInfo(init, token).Symbol is IMethodSymbol target &&
                    (model.IsCompositionMethod(target) || SymbolEqualityComparer.Default.Equals(target.ContainingType, ctor.ContainingType)))
                    AddCallEffects(target, arguments, summary, false);
            }
        }

        private void ReadMemberInitializers(IMethodSymbol ctor, Effects summary)
        {
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
            // Lifecycle entry points have no parameters. A virtual override that consumes
            // a callback needs its caller's delegate, which the virtual-call metadata omits.
            if (summary.ConfigurationParameters.Length != 0 || summary.ConditionalConfigurationParameters.Length != 0)
                yield return new FixedProducerSelector { Error = $"cannot bind the configuration callback invoked by virtual feature method '{method}'. Move the selection into the override or use a non-virtual helper" };
            yield return conditional ? new FixedProducerSelector
            {
                ConditionalFeatures = Types(summary.Features.Concat(summary.ConditionalFeatures)), Hosted = summary.Hosted, Error = summary.Error,
                Resolutions = summary.Resolutions.Select(step => new SlotResolution(step.Type, conditional: true, step.Factory, step.Callback)).ToArray()
            } : summary;
            foreach (var (call, guarded) in summary.VirtualCalls.Select(call => (call, conditional))
                         .Concat(summary.ConditionalVirtualCalls.Select(call => (call, true))))
            {
                var implementation = call.ReceiverParameter == null ? FindOverride(feature, call.MethodKey) : null;
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
                    when receiver.Type?.TypeKind == TypeKind.Delegate:
                    Visit(receiver, result, conditional);
                    var target = DelegateTargets.Resolve(receiver, model._compilation, token,
                        receiver is ILocalReferenceOperation captured ? DelegateTargets.CapturedRead(captured, _delegateCalls) : receiver.Syntax);
                    if (target?.ConstantValue is { HasValue: true, Value: null }) return;
                    foreach (var argument in invoke.Arguments)
                        Visit(argument.Value, result, conditional || target is not (IAnonymousFunctionOperation or IMethodReferenceOperation));
                    InvokeDelegate(receiver, invoke.Arguments, result, conditional);
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
                case IObjectCreationOperation creation:
                    foreach (var argument in creation.Arguments) Visit(argument.Value, result, conditional);
                    if (creation.Constructor is {} ctor && model.IsCompositionMethod(ctor))
                        AddCallEffects(ctor, creation.Arguments, result, conditional, constructing: true);
                    if (creation.Initializer != null) Visit(creation.Initializer, result, conditional);
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
            var dispatch = CallsVirtualAccessor(property, method);
            if (dispatch && property.Instance is IParameterReferenceOperation parameter && !parameter.Type!.IsSealed && FromInput(parameter))
            {
                if (ConfiguredSlot(parameter) is {} slot && BindVirtualMethod(method, slot) is {} implementation)
                {
                    AddCallEffects(implementation, property.Arguments, result, conditional, foreignReceiver: true);
                    return;
                }
                if (result.Owns(parameter.Parameter))
                {
                    result.AddVirtualCall(method, conditional, parameter.Parameter.Name);
                    return;
                }
            }
            VisitMethod(method, dispatch, property.Instance, property.Arguments, result, conditional);
        }

        private static INamedTypeSymbol? ConfiguredSlot(IParameterReferenceOperation parameter)
        {
            for (var parent = parameter.Parent; parent != null; parent = parent.Parent)
            {
                if (parent is not IAnonymousFunctionOperation lambda || !SymbolEqualityComparer.Default.Equals(lambda.Symbol, parameter.Parameter.ContainingSymbol)) continue;
                var argument = lambda.Parent;
                while (argument is IDelegateCreationOperation or IConversionOperation) argument = argument.Parent;
                return argument is IArgumentOperation { Parameter.Name: "configure", Parent: IInvocationOperation registration } &&
                    Registration(registration) is { Hosted: false, Type: INamedTypeSymbol feature } ? feature : null;
            }
            return null;
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
                if (_delegateCalls.Any(frame => frame.Function.SyntaxTree == call.Syntax.SyntaxTree && frame.Function.Span.Contains(call.Syntax.Span)) &&
                    (call.Instance ?? call.Arguments.FirstOrDefault(arg => arg.Parameter?.Ordinal == 0)?.Value) is {} module && !FromInput(module))
                {
                    result.Error ??= $"cannot establish the module used by captured feature registration '{call.Syntax}'";
                    return;
                }
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
                if (call.Instance != null) InvokeDelegate(call.Instance, call.Arguments, result, conditional);
                return;
            }
            var fullMethod = method.ReducedFrom ?? method;
            if (fullMethod.Name == "Apply" && fullMethod.ContainingType.ToDisplayString() == "Elsa.Features.Services.IModule" &&
                fullMethod.ContainingAssembly.Name == "Elsa.Features")
                result.AddResolution(new SlotResolution(null, conditional));
            if (fullMethod.ContainingType.ContainingAssembly.Name == "Elsa.Features" &&
                (fullMethod.ContainingType.ToDisplayString() == "Elsa.Features.Services.IModule" ||
                 fullMethod.ContainingType.ToDisplayString() == "Elsa.Features.Abstractions.FeatureBase" && !call.IsVirtual))
                return;
            if (!model.IsCompositionMethod(fullMethod) && method.MethodKind != MethodKind.LocalFunction)
                return;
            if (fullMethod.Name == "Use" && fullMethod.ContainingType.ToDisplayString() == "Elsa.Extensions.DependencyInjectionExtensions"
                && fullMethod.ContainingAssembly.Name == "Elsa.Features")
                return;
            VisitMethod(method, call.IsVirtual, call.Instance, call.Arguments, result, conditional);
        }

        private void VisitMethod(IMethodSymbol method, bool isVirtual, IOperation? instance, IEnumerable<IArgumentOperation> arguments, Effects result, bool conditional)
        {
            var foreign = instance != null && instance is not IInstanceReferenceOperation;
            if (!isVirtual || !RequiresVirtualDispatch(method))
                AddCallEffects(method, arguments, result, conditional, foreignReceiver: foreign);
            else if (instance?.Type is INamedTypeSymbol { IsSealed: true } receiver && BindVirtualMethod(method, receiver) is {} implementation)
                AddCallEffects(implementation, arguments, result, conditional, foreignReceiver: foreign);
            else if (instance is IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ContainingTypeInstance } && model.CompositionType(method.ContainingType))
                result.AddVirtualCall(method, conditional);
            else
                result.Error ??= $"cannot bind virtual feature method '{method}' to a concrete receiver";
        }

        private void AddCallEffects(IMethodSymbol method, IEnumerable<IArgumentOperation> arguments, Effects result, bool conditional, bool constructing = false, bool foreignReceiver = false)
        {
            var effects = Read(method);
            if ((constructing || foreignReceiver) && effects.HasVirtualCalls)
            {
                result.Error ??= $"cannot bind nested virtual feature selections in '{method}' to the feature being configured";
                return;
            }
            result.Add(effects, conditional, (name, guarded) =>
            {
                var parameter = method.Parameters.FirstOrDefault(parameter => parameter.Name == name);
                var argument = parameter == null ? null : arguments.FirstOrDefault(arg => arg.Parameter?.Ordinal == parameter.Ordinal);
                if (argument != null)
                {
                    if (arguments.Any(arg => model.CompositionType(arg.Value.Type) && !FromInput(arg.Value)))
                        result.Error ??= $"cannot establish the module passed to a configuration callback in '{method}'";
                    else
                        Callback(argument.Value, result, guarded, IsConfigurationDelegate(argument.Parameter!.Type));
                }
                // A local function can invoke a callback captured from its enclosing method.
                else if (method.MethodKind == MethodKind.LocalFunction)
                    result.AddResolution(new SlotResolution(null, guarded, callback: name));
            }, call =>
            {
                var receiver = arguments.FirstOrDefault(arg => arg.Parameter?.Name == call.ReceiverParameter)?.Value;
                while (receiver is IConversionOperation { OperatorMethod: null } conversion) receiver = conversion.Operand;
                return receiver is IParameterReferenceOperation p && result.Owns(p.Parameter) && FromInput(p)
                    ? new VirtualCall(call.MethodKey, call.Arguments, p.Parameter.Name) : null;
            });
        }

        private bool IsConfigurationDelegate(ITypeSymbol type) => type is INamedTypeSymbol { DelegateInvokeMethod: {} invoke } &&
            invoke.Parameters.Any(p => model.CompositionType(p.Type));

        private void InvokeDelegate(IOperation receiver, IEnumerable<IArgumentOperation> arguments, Effects result, bool conditional)
        {
            if (arguments.Any(arg => model.CompositionType(arg.Parameter?.Type) && !FromInput(arg.Value)))
                result.Error ??= $"cannot establish the module passed to configuration callback '{receiver.Syntax}'";
            else
                Callback(receiver, result, conditional, receiver.Type != null && IsConfigurationDelegate(receiver.Type), arguments);
        }

        // Exported callbacks are attached to the helper's input module in the host.
        // An independent module from a field, property or factory would select the wrong plan.
        private bool FromInput(IOperation? value)
        {
            switch (value)
            {
                case IInstanceReferenceOperation instance:
                    return model.CompositionType(instance.Type);
                case IParameterReferenceOperation parameter:
                    var semantic = model._compilation.GetSemanticModel(parameter.Syntax.SyntaxTree);
                    return model.CompositionType(parameter.Type) && parameter.Parameter.RefKind == RefKind.None &&
                        !HostSelections.FindWrites(parameter.Parameter, semantic).Any();
                case ILocalReferenceOperation local: return FromInput(DelegateTargets.LocalValue(local, model._compilation, token, DelegateTargets.CapturedRead(local, _delegateCalls)));
                case IConversionOperation { OperatorMethod: null } conversion: return FromInput(conversion.Operand);
                case IParenthesizedOperation parentheses: return FromInput(parentheses.Operand);
                case IInvocationOperation call: return FromInput(_returnOwners.ReturnedInput(call));
                case IPropertyReferenceOperation property: return FromInput(_returnOwners.ReturnedInput(property));
                default: return false;
            }
        }

        private void Callback(IOperation op, Effects result, bool conditional, bool required = true, IEnumerable<IArgumentOperation>? arguments = null)
        {
            var read = op is ILocalReferenceOperation captured ? DelegateTargets.CapturedRead(captured, _delegateCalls) : op.Syntax;
            var target = DelegateTargets.Resolve(op, model._compilation, token, read);
            switch (target)
            {
                case IAnonymousFunctionOperation lambda:
                    if (_invoking.Add(lambda.Syntax))
                    {
                        _delegateCalls.Push((lambda.Syntax, read));
                        if (arguments == null || !lambda.Symbol.Parameters.Any(p => p.Type.TypeKind == TypeKind.Delegate))
                            Visit(lambda.Body, result, conditional);
                        else
                        {
                            var body = new Effects(lambda.Symbol);
                            Visit(lambda.Body, body, false);
                            result.Add(body.Finish(), conditional, (name, guarded) =>
                            {
                                var parameter = lambda.Symbol.Parameters.FirstOrDefault(p => p.Name == name);
                                var supplied = parameter == null ? null : arguments.FirstOrDefault(arg => arg.Parameter?.Ordinal == parameter.Ordinal);
                                if (supplied != null) Callback(supplied.Value, result, guarded, IsConfigurationDelegate(parameter!.Type));
                                else result.AddResolution(new SlotResolution(null, guarded, callback: name));
                            });
                        }
                        _delegateCalls.Pop();
                        _invoking.Remove(lambda.Syntax);
                    }
                    break;
                // Generated SDK calls queue their known callbacks on FixedFeatureModule.
                case IPropertyReferenceOperation property when IsAppConfigurator(property.Property): break;
                case IMethodReferenceOperation group:
                    if (required || model.IsCompositionMethod(group.Method) ||
                        SymbolEqualityComparer.Default.Equals(group.Method.ContainingAssembly, model._compilation.Assembly))
                        VisitMethod(group.Method, group.IsVirtual, group.Instance, arguments ?? [], result, conditional);
                    break;
                case IParameterReferenceOperation parameter:
                    result.AddResolution(new SlotResolution(null, conditional, callback: parameter.Parameter.Name));
                    break;
                case IConditionalOperation branch:
                    foreach (var (choice, guarded) in DelegateTargets.Choices(branch))
                        Callback(choice, result, conditional || guarded, required, arguments);
                    break;
                case null when op is ILocalReferenceOperation local:
                    if (required)
                        result.Error ??= $"cannot establish which feature configuration callback '{local.Local.Name}' invokes";
                    else if (_invoking.Add(local.Syntax))
                    {
                        foreach (var possible in DelegateTargets.PossibleValues(local, model._compilation, read))
                            Callback(possible, result, true, false, arguments);
                        _invoking.Remove(local.Syntax);
                    }
                    break;
                default:
                    if (required && op.ConstantValue is not { HasValue: true, Value: null })
                        result.Error ??= $"cannot inspect feature configuration callback '{op.Syntax}'";
                    break;
            }
        }

        private sealed class Effects(IMethodSymbol method)
        {
            public bool Owns(IParameterSymbol parameter) =>
                SymbolEqualityComparer.Default.Equals(parameter.ContainingSymbol.OriginalDefinition, method) ||
                SymbolEqualityComparer.Default.Equals(parameter.ContainingSymbol, method.PartialImplementationPart);

            public readonly List<SelectorType> Features = [];
            private readonly List<SlotResolution> _resolutions = [];
            public readonly List<SelectorType> Hosted = [];
            public readonly List<SelectorType> ConditionalFeatures = [];
            public readonly HashSet<string> Callbacks = new(StringComparer.Ordinal);
            public readonly HashSet<string> ConditionalCallbacks = new(StringComparer.Ordinal);
            public readonly List<VirtualCall> VirtualCalls = [];
            public readonly List<VirtualCall> ConditionalVirtualCalls = [];

            public void AddVirtualCall(IMethodSymbol method, bool conditional, string? receiver = null)
            {
                var arguments = method.TypeArguments.Select(VirtualArgument.From).ToArray();
                if (arguments.Any(argument => !argument.HasValue))
                    Error ??= $"the type arguments for virtual feature callback '{method}' are not statically known";
                else
                    (conditional ? ConditionalVirtualCalls : VirtualCalls).Add(new VirtualCall(MethodKey(method), arguments.Select(argument => argument!.Value).ToArray(), receiver));
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
            public void Add(FixedProducerSelector selector, bool conditional, Action<string, bool>? invoke = null, Func<VirtualCall, VirtualCall?>? forward = null)
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
                foreach (var (call, guarded) in selector.VirtualCalls.Select(call => (call, conditional)).Concat(selector.ConditionalVirtualCalls.Select(call => (call, true))))
                {
                    var bound = call.ReceiverParameter == null ? call : forward?.Invoke(call);
                    if (bound == null) Error ??= $"cannot bind the receiver of virtual feature accessor '{call.MethodKey}'";
                    else (guarded ? ConditionalVirtualCalls : VirtualCalls).Add(bound);
                }
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
