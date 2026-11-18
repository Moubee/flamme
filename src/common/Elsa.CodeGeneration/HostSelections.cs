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
    internal bool Validated { get; private set; }
    private readonly Dictionary<ILocalSymbol, AssignmentExpressionSyntax[]> _writeCache = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<IParameterSymbol, IOperation> _localArguments = new(SymbolEqualityComparer.Default);
    private Dictionary<ITypeParameterSymbol, ITypeSymbol> _typeArguments = new(SymbolEqualityComparer.Default);
    private readonly HashSet<InvocationExpressionSyntax> _collectionRoots = [];
    private readonly HashSet<ISymbol> _activeCallbacks = new(SymbolEqualityComparer.Default);
    private readonly INamedTypeSymbol? _module = compilation.GetTypeByMetadataName("Elsa.Features.Services.IModule");
    private readonly INamedTypeSymbol? _feature = compilation.GetTypeByMetadataName("Elsa.Features.Services.IFeature");

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
                ReadCallback(configure.Value, semantic, syntax.GetLocation(), new HashSet<ISymbol>(SymbolEqualityComparer.Default), executed == null);
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
        SyntaxNode result = syntax;
        while (result.Parent is MemberAccessExpressionSyntax or InvocationExpressionSyntax or ParenthesizedExpressionSyntax)
            result = result.Parent;
        var tracked = result is ExpressionSyntax expression && IsModuleOrFeature(semantic.GetTypeInfo(expression).Type);
        var localDeclaration = tracked && result.Parent is EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax declaration } ? declaration : null;
        var local = localDeclaration == null ? null : semantic.GetDeclaredSymbol(localDeclaration);
        if (local == null && tracked && result.Parent is AssignmentExpressionSyntax assignment && assignment.Right == result)
            local = semantic.GetSymbolInfo(assignment.Left).Symbol as ILocalSymbol;
        var scope = syntax.Ancestors().FirstOrDefault(n => n is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax or BaseMethodDeclarationSyntax)
                    ?? syntax.SyntaxTree.GetRoot();
        // Track the assigned module by symbol, including calls later in the same method.
        // An unassigned inline root owns only the fluent call immediately above it.
        if (local != null)
            ReadBody(scope, semantic, local, syntax, scope, conditional);
        else
            ReadBody(result, semantic, null, syntax, result, conditional);
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
                var declaration = reference.Method.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(output.CancellationToken);
                if (declaration != null)
                {
                    if (!_activeCallbacks.Add(reference.Method))
                    {
                        Error(callSite, $"A feature configurator recursively calls '{reference.Method}'.");
                        return;
                    }
                    ReadMethodBody(reference.Method, declaration, semantic, reference.Method.OriginalDefinition.Parameters[0], null, conditional);
                    _activeCallbacks.Remove(reference.Method);
                }
                else if (producers.GetSelector(BindMethod(reference.Method)) is { } selector)
                    AddSelector(selector, reference.Method, callSite, conditional);
                else
                    break;
                return;
            case ILocalReferenceOperation local when visited.Add(local.Local):
                if (Writes(local.Local, semantic).Any(write => callSite.SourceTree != write.SyntaxTree || write.SpanStart < callSite.SourceSpan.Start))
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
        foreach (var assignment in body.DescendantNodesAndSelf().OfType<AssignmentExpressionSyntax>())
        {
            if (!InvokedByComposition(assignment, body, semantic, out var inConditionalCallback) ||
                semantic.GetOperation(assignment.Left) is not IPropertyReferenceOperation { Property.SetMethod: { } setter } property ||
                producers.GetSelector(setter) is not { } selector || !AffectsComposition(selector))
                continue;
            var runs = Execution(assignment, boundary, creation, semantic);
            if (runs == false) continue;
            var inputs = new List<(ISymbol Parameter, IOperation Value)>();
            if (property.Instance is { } receiver && IsModuleOrFeature(receiver.Type))
                inputs.Add((setter.ContainingType, receiver));
            foreach (var index in property.Arguments)
                if (index.Parameter != null && IsModuleOrFeature(index.Value.Type))
                    inputs.Add((setter.Parameters[index.Parameter.Ordinal], index.Value));
            if (semantic.GetOperation(assignment.Right) is { } value && IsModuleOrFeature(value.Type))
                inputs.Add((setter.Parameters.Last(), value));
            var bound = new HashSet<ISymbol>(inputs.Where(input => Owned(input.Value)).Select(input => input.Parameter), SymbolEqualityComparer.Default);
            if (inputs.Count == 0 ? !IsOwned(property.Instance, new HashSet<ISymbol>(SymbolEqualityComparer.Default)) : bound.Count == 0)
                continue;
            var conditional = runs == null || ambientCondition || inConditionalCallback;
            if (bound.Count == inputs.Count)
                AddSelector(selector, setter, assignment.GetLocation(), conditional);
            else if (SymbolEqualityComparer.Default.Equals(setter.ContainingAssembly, compilation.Assembly) && selector.Error == null &&
                     (!setter.IsVirtual && !setter.IsOverride || setter.IsSealed || setter.ContainingType.IsSealed) &&
                     setter.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(output.CancellationToken) is AccessorDeclarationSyntax accessor &&
                     _activeCallbacks.Add(setter))
            {
                ReadBody(accessor, compilation.GetSemanticModel(accessor.SyntaxTree), null, null, accessor, conditional, bound);
                _activeCallbacks.Remove(setter);
            }
            else if (reportedOwnershipErrors.Add(assignment))
                Error(assignment.GetLocation(), $"Cannot assign the effects of '{property.Property}' to independent feature modules. Use a separate helper call per module.");
        }
        foreach (var call in body.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>()
                     .OrderBy(call => call.Span.End).ThenByDescending(call => call.SpanStart))
        {
            if (call == creation || !InvokedByComposition(call, body, semantic, out var inConditionalCallback) || semantic.GetOperation(call) is not IInvocationOperation invocation)
                continue;
            var method = invocation.TargetMethod.ReducedFrom ?? invocation.TargetMethod;
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
                    ReadMethodBody(invocation.TargetMethod, localFunction, semantic, owner, creation, conditionalCall, helperOwners);
                    foreach (var parameter in parameters)
                        _localArguments.Remove(parameter);
                    _activeCallbacks.Remove(method);
                }
                continue;
            }
            var api = SymbolEqualityComparer.Default.Equals(method.ContainingType, _module) ||
                      method.ContainingType.ContainingAssembly.Name == "Elsa.Features" &&
                      method.ContainingType.ToDisplayString() is "Elsa.Extensions.DependencyInjectionExtensions" or "Elsa.Features.Implementations.Module";
            var selectedMethod = producers.GetSelector(BindMethod(invocation.TargetMethod));
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
            if (method.Name == "Apply" && SymbolEqualityComparer.Default.Equals(method.ContainingType, _module))
                continue;
            var executed = Execution(call, boundary, creation, semantic);
            if (executed == false)
                continue;
            var conditional = executed == null || ambientCondition || inConditionalCallback;
            if (api && method.Name is "Configure" or "Use")
            {
                if (BindType(invocation.TargetMethod.TypeArguments.FirstOrDefault()) is not INamedTypeSymbol feature || !FeatureBinding.IsClosed(feature))
                {
                    Error(call.GetLocation(), $"'{method.Name}' requires a closed feature type at this composition site.");
                    continue;
                }
                var custom = method.Name == "Configure" && method.Parameters.Any(parameter => parameter.Name == "factory");
                var immediate = custom && CheckFactory(invocation, feature, semantic, creation);
                Features.Add(new Request(feature, call.GetLocation(), conditional, custom, immediate));
                ReadArguments(invocation, conditional, name => name == "configure");
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
                AddSelector(selector, method, call.GetLocation(), conditional);
                ReadArguments(invocation, conditional, selector.ConfigurationParameters.Contains);
                ReadArguments(invocation, true, selector.ConditionalConfigurationParameters.Contains);
            }
            else if (!api && method.MethodKind != MethodKind.DelegateInvoke && method.ContainingAssembly != null &&
                     !SymbolEqualityComparer.Default.Equals(method.ContainingAssembly, compilation.Assembly) &&
                     producers.IsCompositionMethod(method.OriginalDefinition))
                Error(call.GetLocation(), $"The package method '{method}' needs compiler-visible feature-selection metadata. Rebuild the package with the Elsa generator.");
        }

        bool Owned(IOperation value) => IsOwned(value, new HashSet<ISymbol>(SymbolEqualityComparer.Default));

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

        void ReadArguments(IInvocationOperation invocation, bool conditional, Func<string, bool> invoked)
        {
            foreach (var argument in invocation.Arguments)
            {
                if (argument.IsImplicit || argument.Parameter == null || !invoked(argument.Parameter.Name))
                    continue;
                var value = argument.Value;
                while (value is IDelegateCreationOperation or IConversionOperation)
                    value = value is IDelegateCreationOperation d ? d.Target : ((IConversionOperation)value).Operand;
                // Inline bodies are visited in lexical scope above. Opaque delegates can
                // configure admitted slots, but cannot add an unplanned slot at runtime.
                if (value is IMethodReferenceOperation or ILocalReferenceOperation)
                    ReadCallback(value, semantic, argument.Syntax.GetLocation(), new HashSet<ISymbol>(SymbolEqualityComparer.Default), conditional);
            }
        }

        bool Ambiguous(AssignmentExpressionSyntax? write, ILocalReferenceOperation reference)
        {
            if (write == null || !IsModuleOrFeature(reference.Type) || !AmbiguousWrite(write, reference.Syntax, semantic))
                return false;
            if (reportedOwnershipErrors.Add(write))
                Error(write.GetLocation(), $"The module or feature alias '{reference.Local.Name}' is reassigned conditionally. Use a distinct local for each fixed composition.");
            return true;
        }

        bool IsOwned(IOperation? value, HashSet<ISymbol> seen)
        {
            if (value == null)
                return false;
            if (value.Syntax is InvocationExpressionSyntax root && _collectionRoots.Contains(root))
                return true;
            switch (value)
            {
                case IConversionOperation convert:
                    return IsOwned(convert.Operand, seen);
                case IParenthesizedOperation parentheses:
                    return IsOwned(parentheses.Operand, seen);
                case ILocalReferenceOperation local:
                    if (SymbolEqualityComparer.Default.Equals(local.Local, owner))
                    {
                        if (creation == null) return true;
                        if (value.Syntax.SpanStart <= creation.SpanStart) return false;
                        var write = Writes(local.Local, semantic).LastOrDefault(a => a.SpanStart < value.Syntax.SpanStart);
                        if (Ambiguous(write, local))
                            return false;
                        return write == null || write.SpanStart < creation.SpanStart || write.Right == creation;
                    }
                    if (!seen.Add(local.Local))
                        return false;
                    var initializer = (local.Local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() as VariableDeclaratorSyntax)?.Initializer?.Value;
                    if (initializer == null || initializer.SyntaxTree != semantic.SyntaxTree)
                        return false;
                    var assigned = Writes(local.Local, semantic).LastOrDefault(a => a.SpanStart < value.Syntax.SpanStart);
                    if (Ambiguous(assigned, local))
                        return false;
                    return IsOwned(semantic.GetOperation(assigned?.Right ?? initializer), seen);
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
                    return outer != null && body.Span.Contains(outer.Span) && IsOwned(Receiver(semantic, outer), seen);
                case IPropertyReferenceOperation member:
                    return IsOwned(member.Instance, seen);
                case IFieldReferenceOperation field:
                    return IsOwned(field.Instance, seen);
                case IInvocationOperation call when call.Syntax is InvocationExpressionSyntax syntax:
                    if (IsModuleOrFeature(call.Type))
                    {
                        var inputs = CompositionInputs(call);
                        if (inputs.Count > 1)
                        {
                            var belongs = inputs.Select(input => IsOwned(input.Value, new HashSet<ISymbol>(seen, SymbolEqualityComparer.Default))).ToArray();
                            if (!belongs.Any(owned => owned)) return false;
                            if (belongs.All(owned => owned)) return true;
                            // A source wrapper returning an argument directly has a known
                            // owner even when its side effects touch two separate modules.
                            var method = call.TargetMethod.ReducedFrom ?? call.TargetMethod;
                            var declaration = SymbolEqualityComparer.Default.Equals(method.ContainingAssembly, compilation.Assembly)
                                ? method.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(output.CancellationToken) as MethodDeclarationSyntax : null;
                            var returned = declaration?.ExpressionBody?.Expression ??
                                (declaration?.Body is { Statements.Count: 1 } block && block.Statements[0] is ReturnStatementSyntax ret ? ret.Expression : null);
                            if (returned != null)
                            {
                                var returnedValue = compilation.GetSemanticModel(returned.SyntaxTree).GetOperation(returned);
                                var parameter = returnedValue is IParameterReferenceOperation arg ? (ISymbol)arg.Parameter :
                                    returnedValue is IInstanceReferenceOperation ? method.ContainingType : null;
                                for (var i = 0; i < inputs.Count; i++)
                                    if (SymbolEqualityComparer.Default.Equals(inputs[i].Parameter, parameter))
                                        return belongs[i];
                            }
                            AmbiguousHelper(call);
                            return false;
                        }
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
        selector.Error != null || selector.Features.Length != 0 || selector.ConditionalFeatures.Length != 0 ||
        selector.Hosted.Length != 0 || selector.ConfigurationParameters.Length != 0 ||
        selector.ConditionalConfigurationParameters.Length != 0 || selector.VirtualCalls.Length != 0 || selector.ConditionalVirtualCalls.Length != 0;

    private void AddSelector(FixedProducerSelector selector, IMethodSymbol method, Location location, bool conditional)
    {
        if (selector.Error != null)
            Error(location, $"Cannot resolve fixed selection in '{method}': {selector.Error}.");
        Features.AddRange(selector.Features.Select(type => new Request(type, location, conditional,
            customFactory: selector.FactoryFeatures.Contains(type, SymbolEqualityComparer.Default))));
        Features.AddRange(selector.ConditionalFeatures.Select(type => new Request(type, location, conditional: true)));
        HostedServices.AddRange(selector.Hosted.Select(type => new Request(type, location)));
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
                continue;
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

    private bool IsModuleOrFeature(ITypeSymbol? type) => type != null && (SymbolEqualityComparer.Default.Equals(type, _module) ||
        SymbolEqualityComparer.Default.Equals(type, _feature) || type.AllInterfaces.Any(candidate =>
            SymbolEqualityComparer.Default.Equals(candidate, _feature) || SymbolEqualityComparer.Default.Equals(candidate, _module)));

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
                case BinaryExpressionSyntax coalesce when coalesce.IsKind(SyntaxKind.CoalesceExpression) && coalesce.Right.Span.Contains(call.Span):
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

    private AssignmentExpressionSyntax[] Writes(ILocalSymbol local, SemanticModel semantic)
    {
        if (!_writeCache.TryGetValue(local, out var writes))
            _writeCache[local] = writes = FindWrites(local, semantic).ToArray();
        return writes;
    }

    internal static bool AmbiguousWrite(AssignmentExpressionSyntax write, SyntaxNode read, SemanticModel model)
    {
        var function = write.Ancestors().FirstOrDefault(node => node is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax or BaseMethodDeclarationSyntax);
        if (function != null && !function.Span.Contains(read.Span))
            return true;
        var common = write.Ancestors().FirstOrDefault(node => node.Span.Contains(read.Span)) ?? model.SyntaxTree.GetRoot();
        if (common is IfStatementSyntax or ConditionalExpressionSyntax)
            common = common.Parent ?? common;
        return Execution(write, common, null, model) != true;
    }

    internal static IEnumerable<AssignmentExpressionSyntax> FindWrites(ILocalSymbol local, SemanticModel semantic) =>
        semantic.SyntaxTree.GetRoot().DescendantNodes().OfType<AssignmentExpressionSyntax>()
            .Where(assignment => assignment.Left is IdentifierNameSyntax id && id.Identifier.ValueText == local.Name &&
                SymbolEqualityComparer.Default.Equals(semantic.GetSymbolInfo(id).Symbol, local));

    private void Error(Location location, string message) =>
        output.ReportDiagnostic(Diagnostic.Create(CompilerDiagnostics.RuntimeSelection, location, message));
}
