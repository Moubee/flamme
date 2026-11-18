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
        var groups = new List<List<RootSite>>();
        var collections = new Dictionary<CollectionPath, List<RootSite>>();
        foreach (var site in sites.OrderBy(x => x.Invocation!.SyntaxTree.FilePath, StringComparer.Ordinal).ThenBy(x => x.Invocation!.SpanStart))
        {
            var invocation = site.Invocation!;
            var semantic = compilation.GetSemanticModel(invocation.SyntaxTree);
            var receiver = HostSelections.Receiver(semantic, invocation);
            var key = site.Name == "CreateModule" ? null : CollectionPath.For(receiver, semantic, (location, local) => output.ReportDiagnostic(Diagnostic.Create(
                CompilerDiagnostics.RuntimeSelection, location, $"The service collection '{local}' is reassigned conditionally. Use a distinct collection local for each fixed composition.")));
            if (key != null && collections.TryGetValue(key, out var existing))
                existing.Add(site);
            else
            {
                var group = new List<RootSite> { site };
                groups.Add(group);
                if (key != null)
                    collections.Add(key, group);
            }
        }
        foreach (var group in groups)
            Build(group);
        return _plans;
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

        internal static CollectionPath? For(IOperation? operation, SemanticModel model, Action<Location, string> ambiguous, HashSet<ISymbol>? seen = null)
        {
            switch (operation)
            {
                case ILocalReferenceOperation local:
                    seen ??= new HashSet<ISymbol>(SymbolEqualityComparer.Default);
                    if (!seen.Add(local.Local)) return null;
                    var declaration = local.Local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() as VariableDeclaratorSyntax;
                    var write = HostSelections.FindWrites(local.Local, model).LastOrDefault(a => a.SpanStart < local.Syntax.SpanStart);
                    if (write != null && HostSelections.AmbiguousWrite(write, local.Syntax, model))
                    {
                        ambiguous(write.GetLocation(), local.Local.Name);
                        return null;
                    }
                    var value = write?.Right ?? declaration?.Initializer?.Value;
                    if (value != null && value.SyntaxTree == model.SyntaxTree &&
                        model.GetOperation(value) is IOperation alias and (IParameterReferenceOperation or ILocalReferenceOperation or IPropertyReferenceOperation or IFieldReferenceOperation))
                        return For(alias, model, ambiguous, seen);
                    return new CollectionPath(local.Local, version: write?.SpanStart ?? -1);
                case IParameterReferenceOperation parameter:
                    return new CollectionPath(parameter.Parameter);
                case IInstanceReferenceOperation instance:
                    return instance.Type == null ? null : new CollectionPath(instance.Type);
                case IPropertyReferenceOperation property:
                    return Member(property.Property, property.Instance);
                case IFieldReferenceOperation field:
                    return Member(field.Field, field.Instance);
                case IConversionOperation conversion:
                    return For(conversion.Operand, model, ambiguous, seen);
                default:
                    return null;
            }
            CollectionPath? Member(ISymbol member, IOperation? instance)
            {
                if (instance == null) return new CollectionPath(member);
                var parent = For(instance, model, ambiguous, seen);
                return parent == null ? null : new CollectionPath(member, parent);
            }
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
            if (!item.HasDefaultFactory && !selection.Features.Any(root => root.CustomFactory && !root.Conditional && SymbolEqualityComparer.Default.Equals(root.Type, item.Type)))
                Error($"Feature '{item.Type}' requires an explicit factory. Select it with Configure<T>(factory, configure).");
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
