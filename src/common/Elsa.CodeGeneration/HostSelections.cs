using Elsa.CodeGeneration.Compiler;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using RootSite = Elsa.CodeGeneration.Generators.ElsaCompositionHostGenerator.RootSite;

namespace Elsa.CodeGeneration.Generators;

/// <summary>Reads feature requests attached to one collection or one fresh module in host source.</summary>
internal sealed class HostSelections(Compilation compilation, FixedProducerModel producers, SourceProductionContext output, HashSet<SyntaxNode> reportedOwnershipErrors)
{
    internal List<Request> Features { get; } = [];
    internal List<Request> HostedServices { get; } = [];
    // ConfigureElsa queues its callback until Apply. Consumers of the returned module
    // can resolve a slot immediately, before any of the queued factories have run.
    private readonly List<(SyntaxNode Site, Request? Request, bool Queued)> _resolutions = [];
    private SyntaxNode? _resolutionSite;
    private bool _queued;

    internal bool HasFactoryBeforeResolution(INamedTypeSymbol type, bool suppliedDuringConfiguration)
    {
        // Declarations in separate methods have no implied execution order. Each
        // independent entry point must be safe when called first.
        var scopes = _resolutions.Where(step => step.Request == null || SymbolEqualityComparer.Default.Equals(step.Request.Type, type))
            .GroupBy(step => step.Site.Ancestors().FirstOrDefault(node => node is BaseMethodDeclarationSyntax or
                LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax) ?? step.Site.SyntaxTree.GetRoot());
        if (!scopes.Any()) return suppliedDuringConfiguration;
        return scopes.All(scope =>
        {
            var pending = new List<Request?>();
            foreach (var step in scope.OrderBy(step => step.Site.Span.End).ThenByDescending(step => step.Site.SpanStart))
            {
                if (step.Queued)
                    pending.Add(step.Request);
                else if (step.Request == null)
                    return SuppliedBy(pending);
                else if (!step.Request.CustomFactory || !step.Request.Conditional)
                    return step.Request.CustomFactory;
            }
            return SuppliedBy(pending);
        });

        bool SuppliedBy(List<Request?> requests)
        {
            foreach (var request in requests)
            {
                if (request?.CustomFactory != true) return false;
                if (!request.Conditional) return true;
            }
            return suppliedDuringConfiguration;
        }
    }

    internal bool Validated { get; private set; }
    private readonly Dictionary<ILocalSymbol, LocalWrite[]> _writeCache = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<IParameterSymbol, IOperation> _localArguments = new(SymbolEqualityComparer.Default);
    private Dictionary<ITypeParameterSymbol, ITypeSymbol> _typeArguments = new(SymbolEqualityComparer.Default);
    private readonly FixedProducerModel.FeatureReturnOwners _returnOwners = new(producers);
    private readonly HashSet<InvocationExpressionSyntax> _collectionRoots = [];
    private readonly Stack<(SyntaxNode Function, SyntaxNode Call)> _localCalls = new();
    private readonly HashSet<ISymbol> _activeCallbacks = new(SymbolEqualityComparer.Default);
    private readonly INamedTypeSymbol? _module = compilation.GetTypeByMetadataName("Elsa.Features.Services.IModule");

    internal sealed class Request(INamedTypeSymbol type, Location location, bool conditional = false, bool customFactory = false, bool immediateFactory = false)
    {
        internal INamedTypeSymbol Type { get; } = type;
        internal Location Location { get; } = location;
        internal bool Conditional { get; } = conditional;
        internal bool CustomFactory { get; } = customFactory;
        internal bool ImmediateFactory { get; } = immediateFactory;
    }

    internal static IOperation? Receiver(SemanticModel model, InvocationExpressionSyntax syntax)
    {
        if (model.GetOperation(syntax) is not IInvocationOperation call)
            return null;
        return call.Instance ?? (call.TargetMethod.IsExtensionMethod
            ? call.Arguments.FirstOrDefault(argument => argument.Parameter?.Ordinal == 0)?.Value : null);
    }

    internal void Read(RootSite site, IReadOnlyList<RootSite> related)
    {
        var syntax = site.Invocation!;
        foreach (var peer in related)
            _collectionRoots.Add(peer.Invocation!);
        var semantic = compilation.GetSemanticModel(syntax.SyntaxTree);
        var operation = (IInvocationOperation)semantic.GetOperation(syntax)!;
        if (site.Name is "ConfigureElsa" or "AddElsa")
        {
            var app = compilation.GetTypeByMetadataName("Elsa.Features.AppFeature");
            if (app != null)
                Features.Add(new Request(app, syntax.GetLocation()));
            bool? executed = true;
            if (related.Count > 1)
            {
                var common = syntax.Ancestors().FirstOrDefault(node => related.All(peer =>
                    peer.Invocation!.SyntaxTree == syntax.SyntaxTree && node.Span.Contains(peer.Invocation.Span)));
                var boundary = common is IfStatementSyntax or ConditionalExpressionSyntax or SwitchStatementSyntax or BlockSyntax
                    ? common.Parent : common;
                var firstRoot = related.Where(peer => peer.Invocation!.SyntaxTree == syntax.SyntaxTree).Min(peer => peer.Invocation!.SpanStart);
                executed = Execution(syntax, boundary ?? syntax.SyntaxTree.GetRoot(), null, semantic, firstRoot);
            }
            if (executed == false) return;
            var configure = operation.Arguments.FirstOrDefault(a => a.Parameter?.Name == "configure");
            if (configure != null && !configure.IsImplicit && configure.Value.ConstantValue is not { HasValue: true, Value: null })
            {
                _resolutionSite = syntax;
                _queued = true;
                ReadCallback(configure.Value, semantic, syntax.GetLocation(), new HashSet<ISymbol>(SymbolEqualityComparer.Default), executed == null);
                _queued = false;
                _resolutionSite = null;
            }
            if (site.Name == "AddElsa") _resolutions.Add((syntax, null, false));
            ReadConsumers(syntax, semantic, executed == null);
            return;
        }

        if (site.HasPolicy)
        {
            var policy = operation.Arguments.FirstOrDefault(a => a.Parameter?.Name == "policy")?.Value.ConstantValue;
            if (policy?.HasValue != true || policy.Value.Value is not (0 or 1))
                Error(syntax.GetLocation(), "CreateModule(policy) needs a constant LegacyV1 or ValidatedV1 policy in a generated host.");
            else
                Validated = policy.Value.Value is 1;
        }

        ReadConsumers(syntax, semantic, false);
    }

    private void ReadConsumers(InvocationExpressionSyntax syntax, SemanticModel semantic, bool conditional)
    {
        // Roslyn omits parentheses and null-forgiving operators from the operation
        // tree. Follow the value through reference conversions and fluent calls.
        var result = semantic.GetOperation(syntax)!;
        while (true)
        {
            switch (result.Parent)
            {
                case IConversionOperation conversion when conversion.Conversion.IsIdentity || conversion.Conversion.IsReference:
                    result = conversion;
                    continue;
                case IParenthesizedOperation parenthesized:
                    result = parenthesized;
                    continue;
                case IMemberReferenceOperation member when member.Instance == result:
                    result = member;
                    continue;
                case IInvocationOperation invocation when invocation.Instance == result:
                    result = invocation;
                    continue;
                case IArgumentOperation argument when argument.Parent is IInvocationOperation or IObjectCreationOperation && IsModuleOrFeature(result.Type):
                    result = argument.Parent;
                    continue;
            }
            break;
        }
        var assigned = result.Parent is IVariableInitializerOperation { Parent: IVariableDeclaratorOperation } ||
                       result.Parent is ISimpleAssignmentOperation { Target: ILocalReferenceOperation } assignment && assignment.Value == result;
        var scope = syntax.Ancestors().FirstOrDefault(n => n is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax or BaseMethodDeclarationSyntax)
                    ?? syntax.SyntaxTree.GetRoot();
        // An assigned value can be widened to object and cast back at a later call.
        // IsOwned resolves its reaching definition when a composition API consumes it.
        if (assigned)
            ReadBody(scope, semantic, null, syntax, scope, conditional);
        else
            ReadBody(result.Syntax, semantic, null, syntax, result.Syntax, conditional);
    }

    private void ReadCallback(IOperation callback, SemanticModel semantic, Location callSite, HashSet<ISymbol> visited, bool conditional = false)
    {
        switch (callback)
        {
            case IDelegateCreationOperation creation:
                ReadCallback(creation.Target, semantic, callSite, visited, conditional);
                return;
            case IConversionOperation conversion:
                ReadCallback(conversion.Operand, semantic, callSite, visited, conditional);
                return;
            case IAnonymousFunctionOperation function when function.Symbol.Parameters.Length == 1:
                ReadBody(function.Syntax, semantic, function.Symbol.Parameters[0], null, function.Syntax, conditional);
                return;
            case IMethodReferenceOperation reference:
                if (reference.Method.Parameters.Length != 1)
                    break;
                var callbackMethod = HostMethod(BindMethod(reference.Method), reference.IsVirtual, reference.Instance);
                if (callbackMethod == null)
                {
                    UnboundVirtual(callSite, reference.Method);
                    return;
                }
                var callbackSelector = producers.GetSelector(callbackMethod);
                if (callbackSelector?.HasVirtualCalls == true)
                {
                    UnboundVirtual(callSite, callbackMethod);
                    return;
                }
                callbackMethod = callbackMethod.PartialImplementationPart ?? callbackMethod;
                var declaration = callbackMethod.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(output.CancellationToken);
                if (declaration != null)
                {
                    if (!_activeCallbacks.Add(reference.Method))
                    {
                        Error(callSite, $"A feature configurator recursively calls '{reference.Method}'.");
                        return;
                    }
                    ReadMethodBody(callbackMethod, declaration, semantic, callbackMethod.OriginalDefinition.Parameters[0], null, conditional);
                    _activeCallbacks.Remove(reference.Method);
                }
                else if (callbackSelector != null)
                    AddSelector(callbackSelector, callbackMethod, callSite, conditional);
                else
                    break;
                return;
            case ILocalReferenceOperation local when visited.Add(local.Local):
                if (Writes(local.Local, semantic).Any(write => callSite.SourceTree != write.Site.SyntaxTree || write.Site.Span.End <= callSite.SourceSpan.Start))
                {
                    Error(callSite, $"The feature configurator '{local.Local.Name}' is reassigned. Use a single fixed callback at this composition site.");
                    return;
                }
                var initializer = (local.Local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() as VariableDeclaratorSyntax)?.Initializer?.Value;
                if (initializer != null && compilation.GetSemanticModel(initializer.SyntaxTree).GetOperation(initializer) is { } value)
                {
                    ReadCallback(value, compilation.GetSemanticModel(initializer.SyntaxTree), callSite, visited, conditional);
                    return;
                }
                break;
        }
        Error(callSite, "The host configurator must expose its fixed feature selection. Use an inline lambda or a source method group and put runtime option callbacks inside it.");
    }

    private void ReadBody(SyntaxNode body, SemanticModel semantic, ISymbol? owner, InvocationExpressionSyntax? creation, SyntaxNode boundary, bool ambientCondition = false, HashSet<ISymbol>? helperOwners = null)
    {
        var outerSite = _resolutionSite;
        var handledCallbacks = new HashSet<SyntaxNode>();
        foreach (var expression in Expressions(body, null))
        {
            _resolutionSite = outerSite ?? (SyntaxNode?)expression.Ancestors().TakeWhile(node => node != body)
                .OfType<AnonymousFunctionExpressionSyntax>().LastOrDefault()?.Ancestors().OfType<InvocationExpressionSyntax>().FirstOrDefault() ?? expression;
            if (expression is not InvocationExpressionSyntax call)
            {
                if (expression.Parent is MemberAccessExpressionSyntax member && member.Name == expression || expression.Parent is MemberBindingExpressionSyntax)
                    continue;
                switch (semantic.GetOperation(expression))
                {
                    case IObjectCreationOperation { Constructor: {} constructor } constructed:
                        ReadConstructor((BaseObjectCreationExpressionSyntax)expression, constructed, constructor);
                        break;
                    case IPropertyReferenceOperation property when property.Property.ReturnsByRef ||
                        property.Parent is not ISimpleAssignmentOperation simple || simple.Target != property:
                        ReadAccessor(expression, property, property.Property.GetMethod);
                        break;
                    case IAssignmentOperation { Target: IPropertyReferenceOperation target } assignment:
                        ReadAccessor(expression, target, target.Property.SetMethod, assignment.Value, assignment is ICoalesceAssignmentOperation);
                        break;
                    case IIncrementOrDecrementOperation { Target: IPropertyReferenceOperation target }:
                        ReadAccessor(expression, target, target.Property.SetMethod);
                        break;
                }
                continue;
            }
            if (call == creation || !InvokedByComposition(call, body, semantic, out var inConditionalCallback) || semantic.GetOperation(call) is not IInvocationOperation invocation)
                continue;
            var method = invocation.TargetMethod.ReducedFrom ?? invocation.TargetMethod;
            if (method.MethodKind == MethodKind.DelegateInvoke && invocation.Instance is {} delegateReceiver)
            {
                var site = (SyntaxNode)call;
                if (delegateReceiver is IConditionalAccessInstanceOperation && call.Ancestors().OfType<ConditionalAccessExpressionSyntax>().FirstOrDefault() is {} access)
                {
                    delegateReceiver = semantic.GetOperation(access.Expression)!;
                    site = access;
                }
                var runs = Execution(site, boundary, creation, semantic);
                if (runs != false) ReadDelegate(delegateReceiver, invocation.Arguments, call, ambientCondition || inConditionalCallback || runs == null);
                continue;
            }
            if (method.MethodKind == MethodKind.LocalFunction && method.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is LocalFunctionStatementSyntax localFunction)
            {
                var runs = Execution(call, boundary, creation, semantic);
                if (runs != false && _activeCallbacks.Add(method))
                {
                    var conditionalCall = runs == null || ambientCondition || inConditionalCallback;
                    // Resolve forwarded arguments only if the local function actually uses
                    // them in a composition operation. Observers can accept any module.
                    var parameters = method.OriginalDefinition.Parameters;
                    foreach (var argument in invocation.Arguments)
                        if (argument.Parameter != null)
                            _localArguments[parameters[argument.Parameter.Ordinal]] = argument.Value;
                    _localCalls.Push((localFunction, call));
                    ReadMethodBody(invocation.TargetMethod, localFunction, semantic, owner, creation, conditionalCall, helperOwners);
                    _localCalls.Pop();
                    foreach (var parameter in parameters)
                        _localArguments.Remove(parameter);
                    _activeCallbacks.Remove(method);
                }
                continue;
            }
            var api = SymbolEqualityComparer.Default.Equals(method.ContainingType, _module) ||
                      method.ContainingType.ContainingAssembly.Name == "Elsa.Features" &&
                      method.ContainingType.ToDisplayString() is "Elsa.Extensions.DependencyInjectionExtensions" or "Elsa.Features.Implementations.Module";
            var hostMethod = HostMethod(BindMethod(invocation.TargetMethod), !api && invocation.IsVirtual && producers.IsCompositionMethod(method), invocation.Instance);
            var selectedMethod = hostMethod == null ? null : producers.GetSelector(hostMethod);
            // Resolve aliases only where their identity affects the plan. Passing an
            // IModule as T to an assertion or collection is ordinary application code.
            if (!api && (selectedMethod == null ? !producers.IsCompositionMethod(method) : !AffectsComposition(selectedMethod)))
                continue;
            var inputs = !api && selectedMethod != null ? CompositionInputs(invocation) : null;
            var receiver = Receiver(semantic, call);
            if (inputs is { Count: > 0 } ? !inputs.Any(input => Owned(input.Value)) :
                !IsOwned(receiver, new HashSet<ISymbol>(SymbolEqualityComparer.Default)) &&
                !invocation.Arguments.Any(arg => !arg.IsImplicit && IsModuleOrFeature(arg.Value.Type) && Owned(arg.Value)))
                continue;
            var executed = Execution(call, boundary, creation, semantic);
            if (executed == false)
                continue;
            var conditional = executed == null || ambientCondition || inConditionalCallback;
            if (hostMethod == null)
            {
                UnboundVirtual(call.GetLocation(), method);
                continue;
            }
            if (method.Name == "Apply" && SymbolEqualityComparer.Default.Equals(method.ContainingType, _module))
            {
                RecordApply(call.GetLocation());
                continue;
            }
            if (api && method.Name is "Configure" or "Use")
            {
                if (BindType(invocation.TargetMethod.TypeArguments.FirstOrDefault()) is not INamedTypeSymbol feature || !FeatureBinding.IsClosed(feature))
                {
                    Error(call.GetLocation(), $"'{method.Name}' requires a closed feature type at this composition site.");
                    continue;
                }
                var custom = method.Name == "Configure" && method.Parameters.Any(parameter => parameter.Name == "factory");
                var immediate = custom && CheckFactory(invocation, feature, semantic, creation);
                Record(new Request(feature, call.GetLocation(), conditional, custom, immediate));
                ReadArguments(invocation.Arguments, conditional, name => name == "configure");
                continue;
            }
            if (api && method.Name == "ConfigureHostedService")
            {
                var service = BindType(invocation.TargetMethod.TypeArguments.FirstOrDefault() ??
                              (invocation.Arguments.FirstOrDefault()?.Value as ITypeOfOperation)?.TypeOperand) as INamedTypeSymbol;
                if (service != null && FeatureBinding.IsClosed(service))
                    HostedServices.Add(new Request(service, call.GetLocation()));
                else
                    Error(call.GetLocation(), "Declare hosted-service implementations using a closed generic registration or typeof(T).");
                continue;
            }
            if (selectedMethod is { } selector)
            {
                if (inputs is { Count: > 1 } && inputs.Any(input => !Owned(input.Value)))
                {
                    ReadIndependentInputs(invocation, inputs, selector, conditional);
                    continue;
                }
                AddSelector(selector, hostMethod, call.GetLocation(), conditional,
                    (name, guarded) => ReadArguments(invocation.Arguments, guarded, parameter => parameter == name), invocation);
            }
            else if (!api && method.MethodKind != MethodKind.DelegateInvoke && method.ContainingAssembly != null &&
                     !SymbolEqualityComparer.Default.Equals(method.ContainingAssembly, compilation.Assembly) &&
                     producers.IsCompositionMethod(method.OriginalDefinition))
                Error(call.GetLocation(), $"The package method '{method}' needs compiler-visible feature-selection metadata. Rebuild the package with the Elsa generator.");
        }

        _resolutionSite = outerSite;

        // Invocation receivers and arguments evaluate first. An inline configuration
        // delegate executes inside the call, after Configure has resolved its own slot.
        IEnumerable<ExpressionSyntax> Expressions(SyntaxNode node, List<SyntaxNode>? deferred)
        {
            if (handledCallbacks.Contains(node)) yield break;
            if (node is AnonymousFunctionExpressionSyntax && deferred != null)
            {
                deferred.Add(node);
                yield break;
            }
            var call = node is InvocationExpressionSyntax or BaseObjectCreationExpressionSyntax;
            var callbacks = call ? new List<SyntaxNode>() : deferred;
            var initializer = (node as BaseObjectCreationExpressionSyntax)?.Initializer;
            foreach (var child in node.ChildNodes())
                if (child != initializer)
                    foreach (var expression in Expressions(child, callbacks))
                        yield return expression;
            if (node is InvocationExpressionSyntax or BaseObjectCreationExpressionSyntax or MemberAccessExpressionSyntax or MemberBindingExpressionSyntax or
                ElementAccessExpressionSyntax or ElementBindingExpressionSyntax or AssignmentExpressionSyntax or
                PrefixUnaryExpressionSyntax or PostfixUnaryExpressionSyntax or IdentifierNameSyntax)
                yield return (ExpressionSyntax)node;
            if (initializer != null)
                foreach (var expression in Expressions(initializer, deferred))
                    yield return expression;
            if (call)
                foreach (var callback in callbacks!)
                    foreach (var expression in Expressions(callback, null))
                        yield return expression;
        }

        bool Owned(IOperation value) => IsOwned(value, new HashSet<ISymbol>(SymbolEqualityComparer.Default));

        void ReadDelegate(IOperation receiver, IEnumerable<IArgumentOperation> arguments, SyntaxNode site, bool conditional)
        {
            var read = receiver is ILocalReferenceOperation captured ? DelegateTargets.CapturedRead(captured, _localCalls) : receiver.Syntax;
            var target = DelegateTargets.Resolve(receiver, compilation, output.CancellationToken, read);
            if (target is IConditionalOperation branch)
            {
                foreach (var (choice, guarded) in DelegateTargets.Choices(branch))
                    ReadDelegate(choice, arguments, site, conditional || guarded);
                return;
            }
            if (target == null && receiver is ILocalReferenceOperation unresolved && _activeCallbacks.Add(unresolved.Local))
            {
                foreach (var possible in DelegateTargets.PossibleValues(unresolved, compilation, read))
                    ReadDelegate(possible, arguments, site, true);
                _activeCallbacks.Remove(unresolved.Local);
                return;
            }

            var lambda = target as IAnonymousFunctionOperation;
            var group = target as IMethodReferenceOperation;
            var method = lambda?.Symbol ?? group?.Method;
            if (method == null) return;
            if (group is { Method.MethodKind: not MethodKind.LocalFunction })
            {
                var inputs = arguments.Where(arg => IsModuleOrFeature(arg.Value.Type)).Select(arg => arg.Value).ToList();
                if (group.Instance is {} bound && IsModuleOrFeature(bound.Type)) inputs.Add(bound);
                if (inputs.Count == 0 || !inputs.Any(Owned)) return;
                var known = HostMethod(method, group.IsVirtual, group.Instance);
                if (known == null) UnboundVirtual(site.GetLocation(), method);
                else if (inputs.Any(arg => !Owned(arg))) Error(site.GetLocation(), $"Cannot assign the delegate '{method}' to independent feature modules.");
                else if (producers.GetSelector(known) is {} selector) AddSelector(selector, known, site.GetLocation(), conditional,
                    (name, guarded) => ReadArguments(arguments.Where(arg => arg.Parameter?.Ordinal == known.Parameters.FirstOrDefault(p => p.Name == name)?.Ordinal), guarded, _ => true));
                else if (producers.IsCompositionMethod(known)) Error(site.GetLocation(), $"Cannot bind the feature selections in delegate '{known}'.");
                return;
            }

            var declaration = lambda?.Syntax ?? method.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(output.CancellationToken);
            if (declaration == null || !_activeCallbacks.Add(method)) return;
            var parameters = method.OriginalDefinition.Parameters;
            foreach (var arg in arguments)
                if (arg.Parameter != null) _localArguments[parameters[arg.Parameter.Ordinal]] = arg.Value;
            _localCalls.Push((declaration, site));
            ReadBody(declaration, compilation.GetSemanticModel(declaration.SyntaxTree), owner, creation, declaration, conditional, helperOwners);
            _localCalls.Pop();
            foreach (var parameter in parameters) _localArguments.Remove(parameter);
            _activeCallbacks.Remove(method);
        }

        void ReadConstructor(BaseObjectCreationExpressionSyntax site, IObjectCreationOperation constructed, IMethodSymbol constructor)
        {
            if (!producers.IsCompositionMethod(constructor)) return;
            var selector = producers.GetSelector(constructor);
            if (selector != null && !AffectsComposition(selector)) return;
            var inputs = constructed.Arguments.Where(arg => IsModuleOrFeature(arg.Value.Type) &&
                arg.Value.ConstantValue is not { HasValue: true, Value: null }).ToArray();
            if (!inputs.Any(input => Owned(input.Value)) || !InvokedByComposition(site, body, semantic, out var inConditionalCallback)) return;
            var runs = Execution(site, boundary, creation, semantic);
            if (runs == false) return;
            if (selector == null)
                Error(site.GetLocation(), $"The constructor '{constructor}' needs compiler-visible feature-selection metadata. Rebuild the package with the Elsa generator.");
            else if (inputs.Any(input => !Owned(input.Value)) || selector.HasVirtualCalls)
            {
                if (reportedOwnershipErrors.Add(site))
                    Error(site.GetLocation(), $"Cannot bind the selections of '{constructor}' to one fixed feature module. Configure the intended module directly.");
            }
            else
            {
                // Initializer expressions execute after the constructor, including when their
                // syntax ends inside this object's span. Use the argument boundary for ordering.
                if (outerSite == null && site.ArgumentList != null) _resolutionSite = site.ArgumentList;
                AddSelector(selector, constructor, site.GetLocation(), ambientCondition || runs == null || inConditionalCallback,
                    (name, guarded) => ReadArguments(constructed.Arguments, guarded, parameter => parameter == name));
            }
        }

        void ReadAccessor(ExpressionSyntax site, IPropertyReferenceOperation property, IMethodSymbol? method, IOperation? value = null, bool conditionalWrite = false)
        {
            if (method == null || FixedProducerModel.IsFrameworkProperty(property.Property) || !producers.IsCompositionMethod(method)) return;
            var accessorMethod = HostMethod(method, FixedProducerModel.CallsVirtualAccessor(property, method), property.Instance);
            var selector = accessorMethod == null ? null : producers.GetSelector(accessorMethod);
            var unresolvedDispatch = accessorMethod == null || selector?.HasVirtualCalls == true;
            if (!unresolvedDispatch && selector != null && !AffectsComposition(selector)) return;
            for (IOperation? parent = property.Parent; parent != null; parent = parent.Parent)
                if (parent is INameOfOperation) return;
            if (!InvokedByComposition(site, body, semantic, out var inConditionalCallback)) return;
            var runs = Execution(site, boundary, creation, semantic);
            if (runs == false) return;
            var inputs = new List<(ISymbol Parameter, IOperation Value)>();
            if (property.Instance is { } receiver && IsModuleOrFeature(receiver.Type))
                inputs.Add((method.ContainingType, receiver));
            foreach (var index in property.Arguments)
                if (index.Parameter != null && IsModuleOrFeature(index.Value.Type))
                    inputs.Add((method.Parameters[index.Parameter.Ordinal], index.Value));
            if (value != null && IsModuleOrFeature(value.Type))
                inputs.Add((method.Parameters.Last(), value));
            var bound = new HashSet<ISymbol>(inputs.Where(input => Owned(input.Value)).Select(input => input.Parameter), SymbolEqualityComparer.Default);
            if (inputs.Count == 0 ? !IsOwned(property.Instance, new HashSet<ISymbol>(SymbolEqualityComparer.Default)) : bound.Count == 0)
                return;
            var conditional = runs == null || ambientCondition || inConditionalCallback || conditionalWrite;
            if (unresolvedDispatch)
                UnboundVirtual(site.GetLocation(), method);
            else if (selector == null)
                Error(site.GetLocation(), $"The property accessor '{method}' needs compiler-visible feature-selection metadata. Rebuild the package with the Elsa generator.");
            else if (bound.Count == inputs.Count)
                AddSelector(selector, method, site.GetLocation(), conditional);
            else if (SymbolEqualityComparer.Default.Equals(method.ContainingAssembly, compilation.Assembly) && selector.Error == null &&
                     (!method.IsVirtual && !method.IsOverride || method.IsSealed || method.ContainingType.IsSealed) &&
                     method.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(output.CancellationToken) is { } accessor && _activeCallbacks.Add(method))
            {
                ReadBody(accessor, compilation.GetSemanticModel(accessor.SyntaxTree), null, null, accessor, conditional, bound);
                _activeCallbacks.Remove(method);
            }
            else if (reportedOwnershipErrors.Add(site))
                Error(site.GetLocation(), $"Cannot assign the effects of '{property.Property}' to independent feature modules. Use a separate helper call per module.");
        }

        void ReadIndependentInputs(IInvocationOperation invocation, List<(ISymbol Parameter, IOperation Value)> inputs,
            FixedProducerSelector selector, bool conditional)
        {
            var method = invocation.TargetMethod.ReducedFrom ?? invocation.TargetMethod;
            var declaration = SymbolEqualityComparer.Default.Equals(method.ContainingAssembly, compilation.Assembly)
                ? method.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(output.CancellationToken) as MethodDeclarationSyntax : null;
            // Package summaries aggregate effects across parameters. When inputs come
            // from different modules, only a bound source body can assign each effect.
            if (declaration == null || method.ContainingType.IsGenericType ||
                invocation.IsVirtual && !method.IsSealed && !method.ContainingType.IsSealed ||
                selector.Error != null || invocation.Arguments.Any(argument => !argument.IsImplicit &&
                    (selector.ConfigurationParameters.Contains(argument.Parameter?.Name) || selector.ConditionalConfigurationParameters.Contains(argument.Parameter?.Name)) &&
                    argument.Value.ConstantValue is not { HasValue: true, Value: null }))
            {
                AmbiguousHelper(invocation);
                return;
            }

            if (!_activeCallbacks.Add(method))
            {
                AmbiguousHelper(invocation);
                return;
            }
            var bound = new HashSet<ISymbol>(inputs.Where(input => Owned(input.Value)).Select(input => input.Parameter), SymbolEqualityComparer.Default);
            ReadMethodBody(invocation.TargetMethod, declaration, semantic, null, null, conditional, bound);
            _activeCallbacks.Remove(method);
        }

        void ReadArguments(IEnumerable<IArgumentOperation> arguments, bool conditional, Func<string, bool> invoked)
        {
            foreach (var argument in arguments)
            {
                if (argument.IsImplicit || argument.Parameter == null || !invoked(argument.Parameter.Name))
                    continue;
                var value = argument.Value;
                if (argument.Parameter.Type is INamedTypeSymbol { DelegateInvokeMethod: {} delegateMethod } && !delegateMethod.Parameters.Any(p => IsModuleOrFeature(p.Type)))
                {
                    if (DelegateTargets.Resolve(value, compilation, output.CancellationToken) is IAnonymousFunctionOperation deferred)
                        handledCallbacks.Add(deferred.Syntax);
                    ReadDelegate(value, [], argument.Syntax, conditional);
                    continue;
                }
                while (value is IDelegateCreationOperation or IConversionOperation)
                    value = value is IDelegateCreationOperation d ? d.Target : ((IConversionOperation)value).Operand;
                if (value is IAnonymousFunctionOperation function)
                {
                    handledCallbacks.Add(function.Syntax);
                    var bound = new HashSet<ISymbol>(helperOwners ?? [], SymbolEqualityComparer.Default);
                    foreach (var parameter in function.Symbol.Parameters) bound.Add(parameter);
                    ReadBody(function.Syntax, semantic, owner, creation, function.Syntax, conditional, bound);
                }
                else if (value is IMethodReferenceOperation or ILocalReferenceOperation)
                    ReadCallback(value, semantic, argument.Syntax.GetLocation(), new HashSet<ISymbol>(SymbolEqualityComparer.Default), conditional);
            }
        }

        bool IsOwned(IOperation? value, HashSet<ISymbol> seen, bool compositionRead = false)
        {
            if (value == null)
                return false;
            if (value.Syntax is InvocationExpressionSyntax root && _collectionRoots.Contains(root))
                return true;
            switch (value)
            {
                case IConversionOperation convert:
                    return IsOwned(convert.Operand, seen, compositionRead || IsModuleOrFeature(convert.Type));
                case IParenthesizedOperation parentheses:
                    return IsOwned(parentheses.Operand, seen, compositionRead);
                case ILocalReferenceOperation local:
                    if (creation == null && SymbolEqualityComparer.Default.Equals(local.Local, owner)) return true;
                    var declaration = local.Local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
                    // A local function reads captured variables when called. Locals declared
                    // inside it still use the ordinary reaching definition at the reference.
                    var read = DelegateTargets.CapturedRead(local, _localCalls);
                    var write = Writes(local.Local, semantic).LastOrDefault(a => a.Site.Span.End <= read.SpanStart);
                    if (write != null && (write.Value == null || AmbiguousWrite(write.Site, read, semantic)))
                    {
                        if ((compositionRead || IsModuleOrFeature(local.Type)) && reportedOwnershipErrors.Add(write.Site))
                            Error(write.Site.GetLocation(), $"The module or feature alias '{local.Local.Name}' is reassigned conditionally or through unknown storage. Use a distinct local for each fixed composition.");
                        return false;
                    }
                    var initializer = (declaration as VariableDeclaratorSyntax)?.Initializer?.Value;
                    var source = write?.Value ?? initializer;
                    // Each definition precedes the read. Following its RHS also handles fluent
                    // self-assignment and simultaneous deconstruction without reading the new values early.
                    return source != null && source.SyntaxTree == semantic.SyntaxTree && source.Span.End <= read.SpanStart &&
                        IsOwned(ExpressionOperation(semantic, source), seen, compositionRead || IsModuleOrFeature(local.Type));
                case IInstanceReferenceOperation instance:
                    return instance.Type != null && helperOwners?.Contains(instance.Type) == true;
                case IParameterReferenceOperation parameter:
                    if (helperOwners?.Contains(parameter.Parameter) == true)
                        return true;
                    if (_localArguments.TryGetValue(parameter.Parameter, out var forwarded) && seen.Add(parameter.Parameter))
                        return IsOwned(forwarded, seen);
                    if (SymbolEqualityComparer.Default.Equals(parameter.Parameter, owner))
                        return true;
                    if (!IsModuleOrFeature(parameter.Type) || !seen.Add(parameter.Parameter))
                        return false;
                    var lambda = parameter.Syntax.Ancestors().OfType<AnonymousFunctionExpressionSyntax>().FirstOrDefault(l =>
                        semantic.GetOperation(l) is IAnonymousFunctionOperation f && SymbolEqualityComparer.Default.Equals(f.Symbol, parameter.Parameter.ContainingSymbol));
                    var outer = lambda?.Ancestors().OfType<InvocationExpressionSyntax>().FirstOrDefault();
                    if (outer == null || !body.Span.Contains(outer.Span) || semantic.GetOperation(outer) is not IInvocationOperation invocation)
                        return false;
                    var inputs = CompositionInputs(invocation);
                    return inputs.Count > 0 && inputs.All(input => IsOwned(input.Value, new HashSet<ISymbol>(seen, SymbolEqualityComparer.Default)));
                case IPropertyReferenceOperation member when IsModuleOrFeature(member.Type):
                    var origin = _returnOwners.ReturnedInput(member);
                    if (origin != null) return IsOwned(origin, seen);
                    if ((IsOwned(member.Instance, seen) || member.Arguments.Any(argument => Owned(argument.Value))) && reportedOwnershipErrors.Add(member.Syntax))
                        Error(member.Syntax.GetLocation(), $"Cannot establish which module '{member.Property}' returns. Configure the intended module directly.");
                    return false;
                case IPropertyReferenceOperation member:
                    return IsOwned(member.Instance, seen);
                case IFieldReferenceOperation field when IsModuleOrFeature(field.Type):
                    if (IsOwned(field.Instance, seen) && reportedOwnershipErrors.Add(field.Syntax))
                        Error(field.Syntax.GetLocation(), $"Cannot establish which module '{field.Field}' contains. Configure the intended module directly.");
                    return false;
                case IFieldReferenceOperation field:
                    return IsOwned(field.Instance, seen);
                case IInvocationOperation call when call.Syntax is InvocationExpressionSyntax syntax:
                    if (IsModuleOrFeature(call.Type))
                    {
                        var input = _returnOwners.ReturnedInput(call);
                        if (input != null) return IsOwned(input, seen);
                        if (CompositionInputs(call).Any(arg => IsOwned(arg.Value, new HashSet<ISymbol>(seen, SymbolEqualityComparer.Default))) &&
                            reportedOwnershipErrors.Add(syntax))
                            Error(syntax.GetLocation(), $"Cannot establish which module '{call.TargetMethod}' returns. Configure the intended module directly or return its input explicitly from the helper.");
                        return false;
                    }
                    return IsOwned(Receiver(semantic, syntax), seen);
                case IConditionalAccessInstanceOperation access:
                    var conditional = access.Syntax.AncestorsAndSelf().OfType<ConditionalAccessExpressionSyntax>().FirstOrDefault();
                    return conditional != null && IsOwned(semantic.GetOperation(conditional.Expression), seen);
                default:
                    return false;
            }
        }
    }

    private ITypeSymbol? BindType(ITypeSymbol? type) => type is ITypeParameterSymbol parameter && _typeArguments.TryGetValue(parameter, out var actual) ? actual : type;

    private IMethodSymbol BindMethod(IMethodSymbol method)
    {
        if (!method.IsGenericMethod || _typeArguments.Count == 0) return method;
        var arguments = method.TypeArguments.Select(type => BindType(type)!).ToArray();
        return arguments.SequenceEqual(method.TypeArguments, SymbolEqualityComparer.Default) ? method : method.ConstructedFrom.Construct(arguments);
    }

    private void ReadMethodBody(IMethodSymbol method, SyntaxNode declaration, SemanticModel semantic, ISymbol? owner, InvocationExpressionSyntax? creation, bool conditional, HashSet<ISymbol>? helperOwners = null)
    {
        var previous = _typeArguments;
        if (method.IsGenericMethod)
        {
            method = BindMethod(method);
            _typeArguments = new(previous, SymbolEqualityComparer.Default);
            var parameters = (method.ReducedFrom ?? method).OriginalDefinition.TypeParameters;
            for (var i = 0; i < parameters.Length; i++)
                _typeArguments[parameters[i]] = method.TypeArguments[i];
        }
        if (semantic.SyntaxTree != declaration.SyntaxTree)
            semantic = compilation.GetSemanticModel(declaration.SyntaxTree);
        ReadBody(declaration, semantic, owner, creation, declaration, conditional, helperOwners);
        _typeArguments = previous;
    }

    private List<(ISymbol Parameter, IOperation Value)> CompositionInputs(IInvocationOperation invocation)
    {
        var inputs = new List<(ISymbol, IOperation)>();
        var method = invocation.TargetMethod;
        var declared = (method.ReducedFrom ?? method).OriginalDefinition;
        if (invocation.Instance is { } instance && IsModuleOrFeature(instance.Type))
            inputs.Add((method.ReducedFrom == null ? declared.ContainingType : declared.Parameters[0], instance));
        foreach (var argument in invocation.Arguments)
            if (argument.Parameter != null && IsModuleOrFeature(argument.Value.Type) &&
                argument.Value.ConstantValue is not { HasValue: true, Value: null })
                inputs.Add((declared.Parameters[argument.Parameter.Ordinal + (method.ReducedFrom == null ? 0 : 1)], argument.Value));
        return inputs;
    }

    private void AmbiguousHelper(IInvocationOperation invocation)
    {
        if (reportedOwnershipErrors.Add(invocation.Syntax))
            Error(invocation.Syntax.GetLocation(), $"Cannot assign the effects or result of '{invocation.TargetMethod}' to independent feature modules. Use a separate helper call per module.");
    }

    // A broken default constructor can only be bypassed when the first operation on a
    // fresh module visibly supplies a different, graph-neutral constructor for this slot.
    private bool CheckFactory(IInvocationOperation registration, INamedTypeSymbol feature, SemanticModel semantic, InvocationExpressionSyntax? creation)
    {
        var factory = registration.Arguments.FirstOrDefault(argument => argument.Parameter?.Name == "factory")?.Value;
        while (factory is IConversionOperation or IDelegateCreationOperation)
            factory = factory is IConversionOperation convert ? convert.Operand : ((IDelegateCreationOperation)factory).Target;
        if (factory is not IAnonymousFunctionOperation { Syntax: LambdaExpressionSyntax lambda })
            return false;
        foreach (var syntax in lambda.Body.DescendantNodesAndSelf(n => n is not AnonymousFunctionExpressionSyntax and not LocalFunctionStatementSyntax).OfType<BaseObjectCreationExpressionSyntax>())
        {
            if (semantic.GetOperation(syntax) is IObjectCreationOperation { Constructor: { } candidate }
                && SymbolEqualityComparer.Default.Equals(candidate.ContainingType, feature) && producers.GetSelector(candidate) is { } candidateFacts
                && ChangesGraph(candidateFacts))
                output.ReportDiagnostic(Diagnostic.Create(CompilerDiagnostics.InvalidPlan, syntax.GetLocation(),
                    $"Feature factory invokes '{candidate}', which may change the feature graph. Move feature selection to Configure and declare required dependencies."));
        }

        var expression = lambda.Body as ExpressionSyntax ??
            (lambda.Body is BlockSyntax { Statements.Count: 1 } block && block.Statements[0] is ReturnStatementSyntax returned ? returned.Expression : null);
        var result = expression == null ? null : semantic.GetOperation(expression);
        while (result is IConversionOperation converted)
            result = converted.Operand;
        if (result is not IObjectCreationOperation { Constructor: { } constructor } || !SymbolEqualityComparer.Default.Equals(constructor.ContainingType, feature))
            return false;
        var facts = producers.GetSelector(constructor);
        if (facts == null || ChangesGraph(facts))
            return false;
        if (creation == null || creation.Expression is not MemberAccessExpressionSyntax { Name.Identifier.ValueText: "CreateModule" })
            return false;
        if (Receiver(semantic, (InvocationExpressionSyntax)registration.Syntax)?.Syntax == creation)
            return true;
        var createStatement = creation.Ancestors().OfType<StatementSyntax>().FirstOrDefault();
        var configureStatement = registration.Syntax.Ancestors().OfType<StatementSyntax>().FirstOrDefault();
        return registration.Syntax.Parent is ExpressionStatementSyntax or EqualsValueClauseSyntax &&
            Receiver(semantic, (InvocationExpressionSyntax)registration.Syntax) is ILocalReferenceOperation &&
            createStatement != null && configureStatement != null &&
            configureStatement.GetFirstToken().GetPreviousToken() == createStatement.GetLastToken();

        static bool ChangesGraph(FixedProducerSelector facts) => facts.Error != null || facts.Features.Length != 0 ||
            facts.ConditionalFeatures.Length != 0 || facts.Hosted.Length != 0 || facts.VirtualCalls.Length != 0 || facts.ConditionalVirtualCalls.Length != 0;
    }

    private static bool AffectsComposition(FixedProducerSelector selector) =>
        selector.Error != null || selector.Resolutions.Length != 0 || selector.Features.Length != 0 || selector.ConditionalFeatures.Length != 0 ||
        selector.Hosted.Length != 0 || selector.ConfigurationParameters.Length != 0 ||
        selector.ConditionalConfigurationParameters.Length != 0 || selector.HasVirtualCalls;

    private void RecordApply(Location location)
    {
        if (_queued)
            output.ReportDiagnostic(Diagnostic.Create(CompilerDiagnostics.InvalidPlan, location,
                "An SDK configuration callback already runs inside Apply. Remove the recursive Apply call."));
        _resolutions.Add((_resolutionSite!, null, _queued));
    }

    private void Record(Request request)
    {
        Features.Add(request);
        _resolutions.Add((_resolutionSite!, request, _queued));
    }

    private void AddSelector(FixedProducerSelector selector, IMethodSymbol method, Location location, bool conditional, Action<string, bool>? invoke = null, IInvocationOperation? invocation = null)
    {
        // Metadata preserves the order of slot resolutions, but lists virtual calls
        // separately. Only one effectful override can be inserted without guessing.
        var hasOtherResolutions = selector.OrderedResolutions().Any();
        foreach (var (call, guarded) in selector.VirtualCalls.Select(call => (call, conditional))
                     .Concat(selector.ConditionalVirtualCalls.Select(call => (call, true))))
        {
            var effects = invocation == null ? null : AccessorEffects(call, invocation);
            if (effects != null && !AffectsComposition(effects)) continue;
            if (effects == null || hasOtherResolutions || effects.HasVirtualCalls ||
                effects.ConfigurationParameters.Length > 0 || effects.ConditionalConfigurationParameters.Length > 0)
            {
                UnboundVirtual(location, method);
                return;
            }
            AddSelector(effects, method, location, guarded);
            hasOtherResolutions = true;
        }
        if (selector.Error != null)
            Error(location, $"Cannot resolve fixed selection in '{method}': {selector.Error}.");
        foreach (var step in selector.OrderedResolutions())
        {
            if (step.Callback != null) invoke?.Invoke(step.Callback, conditional || step.Conditional);
            else if (step.Type == null) RecordApply(location);
            else if (step.Type.Value.Concrete is { } type)
                Record(new Request(type, location, conditional || step.Conditional, step.Factory));
        }
        HostedServices.AddRange(selector.Hosted.Select(type => new Request(type, location)));
    }

    private FixedProducerSelector? AccessorEffects(VirtualCall accessor, IInvocationOperation invocation)
    {
        if (accessor.ReceiverParameter == null) return null;
        var receiver = invocation.Arguments.FirstOrDefault(arg => arg.Parameter?.Name == accessor.ReceiverParameter)?.Value;
        if (receiver == null && invocation.TargetMethod.ReducedFrom?.Parameters[0].Name == accessor.ReceiverParameter) receiver = invocation.Instance;
        while (receiver is IConversionOperation { OperatorMethod: null } conversion) receiver = conversion.Operand;
        if (receiver?.Type is not INamedTypeSymbol receiverType || !IsModuleOrFeature(receiverType)) return null;

        // These inputs belong to fixed feature slots, which enforce exact types.
        // An unsealed argument is sufficient if every compatible slot dispatches
        // to the same override, or if every implementation is graph-neutral.
        var implementations = producers.Features.Where(f => compilation.ClassifyCommonConversion(f.Type, receiverType).IsImplicit)
            .Select(f => FixedProducerModel.FindOverride(f.Type, accessor.MethodKey)).Distinct<IMethodSymbol?>(SymbolEqualityComparer.Default).ToArray();
        if (implementations.Length == 0 || implementations.Any(i => i == null)) return null;
        var summaries = implementations.Select(i => producers.GetSelector(i!)).ToArray();
        return summaries.Length == 1 || summaries.All(s => s != null && !AffectsComposition(s)) ? summaries[0] : null;
    }

    // A lambda that an options object stores for later cannot contribute to this host's
    // roots. Producer summaries identify the configurator parameters they actually invoke.
    private bool InvokedByComposition(SyntaxNode call, SyntaxNode body, SemanticModel semantic, out bool conditional)
    {
        conditional = false;
        if (call == body)
            return true;
        foreach (var node in call.Ancestors().TakeWhile(node => node != body))
        {
            if (node is LocalFunctionStatementSyntax or BaseMethodDeclarationSyntax)
                return false;
            if (node is not AnonymousFunctionExpressionSyntax lambda)
                continue;
            if (semantic.GetOperation(lambda) is not IAnonymousFunctionOperation function)
                return false;
            IOperation? parent = function.Parent;
            while (parent is IDelegateCreationOperation or IConversionOperation)
                parent = parent.Parent;
            if (parent is not IArgumentOperation argument || parent.Parent is not IInvocationOperation invocation)
                return false;
            var method = invocation.TargetMethod.ReducedFrom ?? invocation.TargetMethod;
            if (method.Name == "Configure" && SymbolEqualityComparer.Default.Equals(method.ContainingType, _module) ||
                method.Name == "Use" && method.ContainingType.ToDisplayString() == "Elsa.Extensions.DependencyInjectionExtensions" &&
                method.ContainingAssembly.Name == "Elsa.Features")
            {
                if (argument.Parameter?.Name != "configure") return false;
                continue;
            }
            var selector = producers.GetSelector(BindMethod(invocation.TargetMethod));
            var parameter = argument.Parameter?.Name;
            if (selector?.ConfigurationParameters.Contains(parameter) == true)
                continue;
            if (selector?.ConditionalConfigurationParameters.Contains(parameter) == true)
                conditional = true;
            else
                return false;
        }
        return true;
    }

    private static IMethodSymbol? HostMethod(IMethodSymbol method, bool isVirtual, IOperation? instance) =>
        !isVirtual || !FixedProducerModel.RequiresVirtualDispatch(method) ? method :
        instance?.Type is INamedTypeSymbol { IsSealed: true } receiver ? FixedProducerModel.BindVirtualMethod(method, receiver) : null;

    private void UnboundVirtual(Location location, IMethodSymbol method) =>
        Error(location, $"Cannot bind the virtual feature selection in '{method}' at this host call. Declare the selections in a nonvirtual helper.");

    private bool IsModuleOrFeature(ITypeSymbol? type) => producers.CompositionType(type);

    // true = unconditional, false = unreachable, null = selected by runtime control flow.
    private static bool? Execution(SyntaxNode call, SyntaxNode boundary, SyntaxNode? creation, SemanticModel semantic, int after = 0)
    {
        var conditional = false;
        for (var node = call.Parent; node != null && node != boundary; node = node.Parent)
        {
            if (creation != null && node.Span.Contains(creation.Span) && node is not BlockSyntax)
                break;
            ExpressionSyntax? test = null;
            bool expected = true;
            switch (node)
            {
                case GlobalStatementSyntax:
                case BlockSyntax:
                    var siblings = node is BlockSyntax block ? block.Statements.ToArray() :
                        ((CompilationUnitSyntax)node.Parent!).Members.OfType<GlobalStatementSyntax>().Select(global => global.Statement);
                    foreach (var earlier in siblings.TakeWhile(statement => statement.Span.End <= call.SpanStart))
                    {
                        if (earlier.SpanStart < after || creation != null && earlier.SpanStart <= creation.SpanStart || !MayExit(earlier))
                            continue;
                        var flow = semantic.AnalyzeControlFlow(earlier);
                        if (flow?.Succeeded != true) continue;
                        if (!flow.EndPointIsReachable) return false;
                        if (flow.ExitPoints.Any(exit => exit is StatementSyntax jump && semantic.AnalyzeControlFlow(jump)?.StartPointIsReachable == true))
                            conditional = true;
                    }
                    break;
                case ConditionalAccessExpressionSyntax access when access.WhenNotNull.Span.Contains(call.Span):
                    if (ExpressionOperation(semantic, access.Expression) is { Type.TypeKind: TypeKind.Delegate } receiver)
                    {
                        var target = DelegateTargets.Resolve(receiver, semantic.Compilation, default);
                        if (target?.ConstantValue is { HasValue: true, Value: null }) return false;
                        if (target is IAnonymousFunctionOperation or IMethodReferenceOperation) break;
                    }
                    conditional = true;
                    break;
                case BinaryExpressionSyntax coalesce when coalesce.IsKind(SyntaxKind.CoalesceExpression) && coalesce.Right.Span.Contains(call.Span):
                case AssignmentExpressionSyntax assignment when assignment.IsKind(SyntaxKind.CoalesceAssignmentExpression) && assignment.Right.Span.Contains(call.Span):
                    conditional = true;
                    break;
                case IfStatementSyntax statement when !statement.Condition.Span.Contains(call.Span):
                    test = statement.Condition;
                    expected = statement.Statement.Span.Contains(call.Span);
                    break;
                case ConditionalExpressionSyntax ternary when !ternary.Condition.Span.Contains(call.Span):
                    test = ternary.Condition;
                    expected = ternary.WhenTrue.Span.Contains(call.Span);
                    break;
                case BinaryExpressionSyntax binary when binary.Right.Span.Contains(call.Span) &&
                    (binary.IsKind(SyntaxKind.LogicalAndExpression) || binary.IsKind(SyntaxKind.LogicalOrExpression)):
                    test = binary.Left;
                    expected = binary.IsKind(SyntaxKind.LogicalAndExpression);
                    break;
                case SwitchSectionSyntax or SwitchExpressionArmSyntax or ForEachStatementSyntax or ForStatementSyntax or WhileStatementSyntax or CatchClauseSyntax:
                    conditional = true;
                    break;
            }
            if (test == null)
                continue;
            var constant = semantic.GetConstantValue(test);
            if (constant is { HasValue: true, Value: bool value })
            {
                if (value != expected)
                    return false;
            }
            else
                conditional = true;
        }
        return conditional ? null : true;

        static bool MayExit(StatementSyntax statement) => statement.DescendantNodesAndSelf(
            node => node is not AnonymousFunctionExpressionSyntax and not LocalFunctionStatementSyntax)
            .Any(node => node is ReturnStatementSyntax or GotoStatementSyntax or BreakStatementSyntax or ContinueStatementSyntax or YieldStatementSyntax);
    }

    private LocalWrite[] Writes(ILocalSymbol local, SemanticModel semantic)
    {
        if (!_writeCache.TryGetValue(local, out var writes))
            _writeCache[local] = writes = FindWrites(local, semantic).ToArray();
        return writes;
    }

    internal static bool AmbiguousWrite(SyntaxNode write, SyntaxNode read, SemanticModel model)
    {
        var function = write.Ancestors().FirstOrDefault(node => node is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax or BaseMethodDeclarationSyntax);
        if (function != null && !function.Span.Contains(read.Span))
            return true;
        var common = write.Ancestors().FirstOrDefault(node => node.Span.Contains(read.Span)) ?? model.SyntaxTree.GetRoot();
        if (common is IfStatementSyntax or ConditionalExpressionSyntax)
            common = common.Parent ?? common;
        return Execution(write, common, null, model) != true;
    }

    internal sealed class LocalWrite(SyntaxNode site, ExpressionSyntax? value)
    {
        internal SyntaxNode Site => site;
        internal ExpressionSyntax? Value => value;
    }

    internal static IEnumerable<LocalWrite> FindWrites(ISymbol local, SemanticModel semantic)
    {
        var writes = new List<LocalWrite>();
        var scope = local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax().Ancestors().FirstOrDefault(
            node => node is BaseMethodDeclarationSyntax or LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax) ?? semantic.SyntaxTree.GetRoot();
        foreach (var node in scope.DescendantNodes())
        {
            if (node is AssignmentExpressionSyntax assignment)
                Visit(assignment.Left, assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ? assignment.Right : null, assignment);
            else if (node is ArgumentSyntax { Parent.Parent: { } call } arg && arg.RefKindKeyword.ValueText is "ref" or "out")
                Visit(arg.Expression, null, call);
            else if (node is RefExpressionSyntax reference)
                Visit(reference.Expression, null, reference);
        }
        return writes.OrderBy(write => write.Site.Span.End);

        void Visit(ExpressionSyntax target, ExpressionSyntax? value, SyntaxNode write)
        {
            while (target is ParenthesizedExpressionSyntax paren) target = paren.Expression;
            while (value is ParenthesizedExpressionSyntax paren) value = paren.Expression;
            if (target is TupleExpressionSyntax tuple)
            {
                var elements = (value as TupleExpressionSyntax)?.Arguments;
                for (var i = 0; i < tuple.Arguments.Count; i++)
                    Visit(tuple.Arguments[i].Expression, elements?.Count == tuple.Arguments.Count ? elements.Value[i].Expression : null, write);
            }
            else if (target is IdentifierNameSyntax id && id.Identifier.ValueText == local.Name &&
                     SymbolEqualityComparer.Default.Equals(semantic.GetSymbolInfo(id).Symbol, local))
                writes.Add(new LocalWrite(write, value));
        }
    }

    internal static IOperation? ExpressionOperation(SemanticModel semantic, ExpressionSyntax expression)
    {
        while (true)
        {
            switch (expression)
            {
                case ParenthesizedExpressionSyntax parentheses:
                    expression = parentheses.Expression;
                    continue;
                case PostfixUnaryExpressionSyntax suppress when suppress.IsKind(SyntaxKind.SuppressNullableWarningExpression):
                    expression = suppress.Operand;
                    continue;
                default:
                    return semantic.GetOperation(expression);
            }
        }
    }

    private void Error(Location location, string message) =>
        output.ReportDiagnostic(Diagnostic.Create(CompilerDiagnostics.RuntimeSelection, location, message));
}
