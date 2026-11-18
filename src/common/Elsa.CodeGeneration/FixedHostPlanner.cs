using Elsa.CodeGeneration.Compiler;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using RootSite = Elsa.CodeGeneration.Generators.ElsaCompositionHostGenerator.RootSite;

namespace Elsa.CodeGeneration.Generators;

/// <summary>Resolves the selected closure once in the host compiler. Each build plan is one sequence of direct slot calls.</summary>
internal sealed class FixedHostPlanner(Compilation compilation, FixedProducerModel producers, SourceProductionContext output)
{
    private readonly Dictionary<INamedTypeSymbol, FixedProducerFeature> _available = new(SymbolEqualityComparer.Default);
    private readonly List<Plan> _plans = [];
    private readonly HashSet<SyntaxNode> _reportedOwnershipErrors = [];

    internal List<Plan> Build(RootSite[] sites)
    {
        foreach (var feature in producers.Features)
            if (!_available.ContainsKey(feature.Type))
                _available.Add(feature.Type, feature);

        // ConfigureElsa accumulates requests for the subsequent AddElsa on this service collection.
        // CreateModule always constructs an independent module, even when the receiver is shared.
        var calls = new Dictionary<IMethodSymbol, IInvocationOperation[]>(SymbolEqualityComparer.Default);
        var groups = new List<List<RootSite>>();
        var collections = new Dictionary<CollectionPath, List<RootSite>>();
        var potentialAliases = new Dictionary<CollectionPath, List<(RootSite Site, IInvocationOperation? Origin)>>();
        foreach (var site in sites.OrderBy(x => x.Invocation!.SyntaxTree.FilePath, StringComparer.Ordinal).ThenBy(x => x.Invocation!.SpanStart))
        {
            var invocation = site.Invocation!;
            var semantic = compilation.GetSemanticModel(invocation.SyntaxTree);
            var receiver = HostSelections.Receiver(semantic, invocation);
            var key = site.Name == "CreateModule" ? null : CollectionPath.For(receiver, semantic, CallsTo, (location, local) => output.ReportDiagnostic(Diagnostic.Create(
                CompilerDiagnostics.RuntimeSelection, location, $"The service collection '{local}' is reassigned conditionally or through unknown storage. Use a distinct collection local for each fixed composition.")), Remember);
            if (key != null) Remember(key, null);
            if (key != null && collections.TryGetValue(key, out var existing))
                existing.Add(site);
            else
            {
                var group = new List<RootSite> { site };
                groups.Add(group);
                if (key != null)
                    collections.Add(key, group);
            }

            void Remember(CollectionPath path, IInvocationOperation? origin)
            {
                if (!potentialAliases.TryGetValue(path, out var uses))
                    potentialAliases.Add(path, uses = []);
                uses.Add((site, origin));
            }
        }
        foreach (var group in groups)
            Build(group);
        var sitePlans = _plans.SelectMany(plan => plan.Sites.Select(site => (site, plan))).ToDictionary(entry => entry.site, entry => entry.plan);
        var reported = new HashSet<SyntaxNode>();
        foreach (var uses in potentialAliases.Values)
        {
            if (uses.All(use => sitePlans[use.Site] == sitePlans[uses[0].Site])) continue;
            foreach (var (_, origin) in uses)
                if (origin != null && reported.Add(origin.Syntax))
                    output.ReportDiagnostic(Diagnostic.Create(CompilerDiagnostics.RuntimeSelection, origin.Syntax.GetLocation(),
                        $"The call to '{origin.TargetMethod.Name}' can share a service collection across different fixed feature plans. " +
                        "Place related Elsa calls on the same collection variable in one method."));
        }
        return _plans;

        IInvocationOperation[] CallsTo(IMethodSymbol method)
        {
            if (calls.TryGetValue(method, out var known)) return known;
            var result = new List<IInvocationOperation>();
            foreach (var tree in compilation.SyntaxTrees)
            {
                var model = compilation.GetSemanticModel(tree);
                foreach (var invocation in tree.GetRoot(output.CancellationToken).DescendantNodes().OfType<InvocationExpressionSyntax>())
                {
                    var name = invocation.Expression switch
                    {
                        MemberAccessExpressionSyntax member => member.Name,
                        MemberBindingExpressionSyntax binding => binding.Name,
                        SimpleNameSyntax simple => simple,
                        _ => null
                    };
                    if (name?.Identifier.ValueText == method.Name && model.GetOperation(invocation, output.CancellationToken) is IInvocationOperation call &&
                        SymbolEqualityComparer.Default.Equals((call.TargetMethod.ReducedFrom ?? call.TargetMethod).OriginalDefinition, method))
                        result.Add(call);
                }
            }
            return calls[method] = result.ToArray();
        }
    }

    // Keep the owning object as part of the key: first.Services and second.Services
    // refer to independently configurable hosts even though Roslyn binds the same property.
    private sealed class CollectionPath(ISymbol symbol, CollectionPath? owner = null, int version = -1) : IEquatable<CollectionPath>
    {
        private ISymbol Symbol => symbol;
        private CollectionPath? Owner => owner;
        private int Version => version;
        public bool Equals(CollectionPath? other) => other != null && SymbolEqualityComparer.Default.Equals(symbol, other.Symbol)
            && version == other.Version && Equals(owner, other.Owner);
        public override bool Equals(object? other) => other is CollectionPath path && Equals(path);
        public override int GetHashCode() => unchecked(SymbolEqualityComparer.Default.GetHashCode(symbol) * 31 + (owner?.GetHashCode() ?? 0) + version);

        internal static CollectionPath? For(IOperation? operation, SemanticModel model, Func<IMethodSymbol, IInvocationOperation[]> callsTo, Action<Location, string> ambiguous,
            Action<CollectionPath, IInvocationOperation?> possible, HashSet<ISymbol>? seen = null)
        {
            switch (operation)
            {
                case ILocalReferenceOperation local:
                    var declaration = local.Local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() as VariableDeclaratorSyntax;
                    var write = HostSelections.FindWrites(local.Local, model).LastOrDefault(a => a.Site.Span.End <= local.Syntax.SpanStart);
                    if (write != null && (write.Value == null || HostSelections.AmbiguousWrite(write.Site, local.Syntax, model)))
                    {
                        ambiguous(write.Site.GetLocation(), local.Local.Name);
                        return null;
                    }
                    var value = write?.Value ?? declaration?.Initializer?.Value;
                    if (value != null && value.SyntaxTree == model.SyntaxTree && value.Span.End <= local.Syntax.SpanStart &&
                        InputPath(HostSelections.ExpressionOperation(model, value)) is { } alias)
                        return alias;
                    return new CollectionPath(local.Local, version: write?.Site.SpanStart ?? -1);
                case IParameterReferenceOperation parameter:
                    // The same helper site may have several callers. Keep its own fixed
                    // plan and diagnose incompatible plans on a collection passed to it.
                    if (IsCollection(parameter.Type) && parameter.Parameter.ContainingSymbol is IMethodSymbol enclosing)
                    {
                        seen ??= new HashSet<ISymbol>(SymbolEqualityComparer.Default);
                        if (seen.Add(parameter.Parameter))
                            foreach (var call in callsTo(enclosing.OriginalDefinition))
                            {
                                var arg = call.Arguments.FirstOrDefault(a => a.Parameter?.Name == parameter.Parameter.Name)?.Value;
                                if (arg == null && call.TargetMethod.ReducedFrom != null && parameter.Parameter.Ordinal == 0) arg = call.Instance;
                                if (arg != null && For(arg, arg.SemanticModel!, callsTo, ambiguous, possible,
                                    new HashSet<ISymbol>(seen, SymbolEqualityComparer.Default)) is { } path)
                                    possible(path, call);
                            }
                    }
                    return new CollectionPath(parameter.Parameter);
                case IInstanceReferenceOperation instance:
                    return instance.Type == null ? null : new CollectionPath(instance.Type);
                case IPropertyReferenceOperation property:
                    return Member(property.Property, property.Instance);
                case IFieldReferenceOperation field:
                    return Member(field.Field, field.Instance);
                case IConversionOperation conversion when conversion.OperatorMethod == null:
                    return InputPath(conversion.Operand);
                case IParenthesizedOperation parentheses:
                    return InputPath(parentheses.Operand);
                case IInvocationOperation call when IsCollection(call.Type):
                    var method = call.TargetMethod.ReducedFrom ?? call.TargetMethod;
                    var receiver = call.Instance ?? (method.IsExtensionMethod ? call.Arguments.FirstOrDefault(a => a.Parameter?.Ordinal == 0)?.Value : null);
                    if (method.ContainingAssembly.Name == "Microsoft.Extensions.Logging" && method.GetDocumentationCommentId() is
                        "M:Microsoft.Extensions.DependencyInjection.LoggingServiceCollectionExtensions.AddLogging(Microsoft.Extensions.DependencyInjection.IServiceCollection)" or
                        "M:Microsoft.Extensions.DependencyInjection.LoggingServiceCollectionExtensions.AddLogging(Microsoft.Extensions.DependencyInjection.IServiceCollection,System.Action{Microsoft.Extensions.Logging.ILoggingBuilder})")
                        return InputPath(receiver);

                    // An opaque factory may return an input, a cached collection or a fresh one.
                    // These paths can diagnose incompatible plans. They never merge them.
                    var owner = InputPath(receiver);
                    possible(new CollectionPath(method.OriginalDefinition, owner), call);
                    if (owner != null && IsCollection(receiver?.Type)) possible(owner, call);
                    foreach (var argument in call.Arguments)
                        if (argument.Value != receiver && IsCollection(argument.Value.Type) && InputPath(argument.Value) is { } input)
                            possible(input, call);
                    return null;
                default:
                    return null;
            }
            CollectionPath? InputPath(IOperation? value) => For(value, model, callsTo, ambiguous, possible,
                seen == null ? null : new HashSet<ISymbol>(seen, SymbolEqualityComparer.Default));

            static bool IsCollection(ITypeSymbol? type) => type != null && (IsCollectionContract(type) || type.AllInterfaces.Any(IsCollectionContract));
            static bool IsCollectionContract(ITypeSymbol type) => type.Name == "IServiceCollection" &&
                type.ContainingAssembly?.Name == "Microsoft.Extensions.DependencyInjection.Abstractions" &&
                type.ContainingNamespace.ToDisplayString() == "Microsoft.Extensions.DependencyInjection";

            CollectionPath? Member(ISymbol member, IOperation? instance)
            {
                var parent = InputPath(instance);
                if (parent == null && instance != null) return null;
                var path = new CollectionPath(member, parent);
                if (!IsCollection(operation!.Type)) return path;

                var scope = operation.Syntax.Ancestors().FirstOrDefault(node => node is BaseMethodDeclarationSyntax or LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax)
                    ?? model.SyntaxTree.GetRoot();
                foreach (var write in scope.DescendantNodes().OfType<AssignmentExpressionSyntax>()
                             .Where(a => a.Span.End <= operation.Syntax.SpanStart).OrderByDescending(a => a.Span.End))
                {
                    var target = model.GetOperation(write.Left) as IMemberReferenceOperation;
                    if (target == null || !SymbolEqualityComparer.Default.Equals(member, target.Member) ||
                        !Equals(parent, InputPath(target.Instance))) continue;
                    if (HostSelections.AmbiguousWrite(write, operation.Syntax, model) ||
                        model.GetOperation(write) is not ISimpleAssignmentOperation ||
                        member is IPropertySymbol property && !IsAutoProperty(property))
                    {
                        ambiguous(write.GetLocation(), member.Name);
                        return null;
                    }
                    return InputPath(HostSelections.ExpressionOperation(model, write.Right)) ?? new CollectionPath(member, parent, write.SpanStart);
                }
                return path;
            }

            static bool IsAutoProperty(IPropertySymbol property) =>
                !property.IsAbstract && (!property.IsVirtual && !property.IsOverride || property.IsSealed || property.ContainingType.IsSealed) &&
                property.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is PropertyDeclarationSyntax { AccessorList: { } list } &&
                list.Accessors.All(a => a.Body == null && a.ExpressionBody == null);
        }
    }

    private void Build(List<RootSite> sites)
    {
        var selection = new HostSelections(compilation, producers, output, _reportedOwnershipErrors);
        foreach (var site in sites)
            selection.Read(site, sites);
        var graph = new List<FixedProducerFeature>();
        var visited = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var path = new List<INamedTypeSymbol>();
        foreach (var request in selection.Features.Where(item => !item.Conditional))
            Visit(request.Type, request.Location);
        // Configure/Apply contributions select additional nodes. They are not prerequisite
        // edges: a feature is allowed to configure itself or a feature that depends on it.
        HashSet<INamedTypeSymbol> present;
        int previousCount;
        do
        {
            previousCount = graph.Count;
            present = new HashSet<INamedTypeSymbol>(graph.Select(feature => feature.Type), SymbolEqualityComparer.Default);
            bool changed;
            do
            {
                var excluded = graph.Where(item => present.Contains(item.Type) && item.Conditional.Any(type => !present.Contains(type))).ToArray();
                changed = excluded.Length != 0;
                foreach (var item in excluded)
                    present.Remove(item.Type);
            } while (selection.Validated && changed);

            foreach (var admitted in graph.Take(previousCount).Where(item => present.Contains(item.Type)).ToArray())
                foreach (var extra in admitted.Introduced)
                    Visit(extra, sites[0].Invocation!.GetLocation());
        } while (previousCount != graph.Count);

        foreach (var request in selection.Features.Where(item => !present.Contains(item.Type)))
            if (request.Conditional)
                Report(CompilerDiagnostics.RuntimeSelection, request.Location,
                    $"The runtime condition selects '{request.Type}'. Declare the feature unconditionally and configure its options at runtime.");
            else
                ReportExcludedRegistration(request.Type, request.Location, "This call");

        foreach (var item in graph.Where(item => present.Contains(item.Type)))
        {
            if (item.ConstructorError != null && !selection.Features.Any(root => root.ImmediateFactory && !root.Conditional && SymbolEqualityComparer.Default.Equals(root.Type, item.Type)))
                Error($"Cannot construct '{item.Type}' in a fixed graph: {item.ConstructorError}.");
            if (item.Error != null)
                Error($"Cannot plan '{item.Type}': {item.Error}.");
            foreach (var requested in item.Introduced.Where(type => !present.Contains(type)))
                ReportExcludedRegistration(requested, sites[0].Invocation!.GetLocation(), $"Feature '{item.Type}'");
            foreach (var conditional in item.ConditionalIntroduced.Where(type => !present.Contains(type)))
                Report(CompilerDiagnostics.RuntimeSelection, sites[0].Invocation!.GetLocation(),
                    $"Feature '{item.Type}' conditionally selects '{conditional}'. Declare '{conditional}' at build time.");
            if (selection.Validated)
                foreach (var dependency in item.Strong.Where(type => !present.Contains(type)))
                    Error($"Feature '{item.Type}' requires '{dependency}', which was excluded by its presence condition.");
            if (!item.HasDefaultFactory && !selection.HasFactoryBeforeResolution(item.Type))
                Error($"Feature '{item.Type}' requires an explicit factory before its first resolution. Call Configure<T>(factory, configure) before Use, default Configure, or Apply.");
        }
        var features = graph.Where(item => present.Contains(item.Type)).ToList();
        var hosted = new List<string>();
        foreach (var request in selection.HostedServices)
        {
            var bridge = producers.GetHostedServiceBridge(request.Type);
            if (bridge == null)
                Error($"No generated hosted-service binding is available for '{request.Type}'.");
            else if (!hosted.Contains(bridge))
                hosted.Add(bridge);
        }
        var plan = _plans.Find(item => item.Features.Select(feature => feature.Bridge).SequenceEqual(features.Select(feature => feature.Bridge))
                                       && item.HostedServices.SequenceEqual(hosted));
        if (plan == null)
        {
            plan = new Plan(features, hosted);
            _plans.Add(plan);
        }
        plan.Sites.AddRange(sites);

        // A conditional dependency can be omitted by the policy, but an actual
        // Configure/Use call needs a slot even when its configuration action is empty.
        void ReportExcludedRegistration(INamedTypeSymbol type, Location location, string caller)
        {
            if (!_available.TryGetValue(type, out var feature)) return; // Visit already diagnosed the missing binding.
            var missing = feature.Conditional.Where(gate => !present.Contains(gate));
            Report(CompilerDiagnostics.InvalidPlan, location,
                $"{caller} configures '{type}', whose DependencyOf condition requires {string.Join(", ", missing.Select(gate => $"'{gate}'"))}. Select the required features or remove the call.");
        }

        void Visit(INamedTypeSymbol type, Location location)
        {
            var cycle = path.FindIndex(ancestor => SymbolEqualityComparer.Default.Equals(ancestor, type));
            if (cycle >= 0)
            {
                Report(CompilerDiagnostics.InvalidPlan, location, "Feature dependency cycle: " + string.Join(" -> ", path.Skip(cycle).Append(type)) + ".");
                return;
            }
            if (!visited.Add(type))
                return;
            if (!_available.TryGetValue(type, out var feature))
            {
                const string reason = "compile the declaring feature library with fixed feature bindings";
                Report(CompilerDiagnostics.InvalidPlan, location,
                    $"No usable fixed binding for '{type}' on path {string.Join(" -> ", path.Append(type))}: {reason}.");
                return;
            }
            path.Add(type);
            foreach (var dependency in feature.Strong)
                Visit(dependency, location);
            path.RemoveAt(path.Count - 1);
            graph.Add(feature);
        }
        void Error(string message) => Report(CompilerDiagnostics.InvalidPlan, sites[0].Invocation!.GetLocation(), message);
        void Report(DiagnosticDescriptor diagnostic, Location location, string message)
            => output.ReportDiagnostic(Diagnostic.Create(diagnostic, location, message));
    }

    internal sealed class Plan(List<FixedProducerFeature> features, List<string> hosted)
    {
        internal List<FixedProducerFeature> Features { get; } = features;
        internal List<string> HostedServices { get; } = hosted;
        internal List<RootSite> Sites { get; } = [];
    }
}
