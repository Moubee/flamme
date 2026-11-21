using System.Collections.Immutable;
using Flamme.ActivityGenerator.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Flamme.ActivityGenerator.Diagnostics.BindingDiagnostics;
using static Flamme.ActivityGenerator.Helpers.ActivitySymbols;

namespace Flamme.ActivityGenerator.Discovery;

internal sealed class ActivityDiscovery
{
    private readonly Compilation compilation;
    private readonly INamedTypeSymbol activity;
    private readonly INamedTypeSymbol contextType;
    private readonly Action<Diagnostic> report;
    private readonly Dictionary<INamedTypeSymbol, bool> descriptors = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<INamedTypeSymbol, Location> explicitSites = new(SymbolEqualityComparer.Default);
    private readonly HashSet<INamedTypeSymbol> inferred = new(SymbolEqualityComparer.Default);
    private readonly HashSet<INamedTypeSymbol> bridgeOwners = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>> explicitContexts = new(SymbolEqualityComparer.Default);

    private ActivityDiscovery(Compilation compilation, INamedTypeSymbol activity, INamedTypeSymbol contextType, Action<Diagnostic> report)
    {
        this.compilation = compilation;
        this.activity = activity;
        this.contextType = contextType;
        this.report = report;
    }

    internal static IncrementalValuesProvider<SyntaxNode> CreateCandidates(SyntaxValueProvider syntax) =>
        syntax.CreateSyntaxProvider(
                static (node, _) => node is TypeDeclarationSyntax or GenericNameSyntax or
                    QualifiedNameSyntax { Right: IdentifierNameSyntax } or InvocationExpressionSyntax,
                static (context, cancellationToken) => GetCandidate(context, cancellationToken))
            .Where(static node => node is not null)
            .Select(static (node, _) => node!)
            .WithTrackingName("ActivityCandidates");

    private static SyntaxNode? GetCandidate(GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        var producer = ActivityCompilation.Get(context.SemanticModel.Compilation);
        if (producer.Activity is not { } activity || producer.ContextType is not { } contextType)
            return null;

        var node = context.Node;
        var model = producer.GetSemanticModel(node.SyntaxTree);
        if (node is TypeDeclarationSyntax declaration)
        {
            if (model.GetDeclaredSymbol(declaration, cancellationToken) is not INamedTypeSymbol type)
                return null;

            return type.AllInterfaces.Contains(activity, SymbolEqualityComparer.Default) || Extends(type, contextType)
                ? declaration : null;
        }

        return GetClosedType(model, node, cancellationToken) is { } closed && CanBind(closed, activity) ? node : null;
    }

    internal static ActivityDiscoveryResult? Discover(Compilation compilation, IEnumerable<SyntaxNode> candidates,
        CancellationToken cancellationToken, Action<Diagnostic> report)
    {
        var producer = ActivityCompilation.Get(compilation);
        if (producer.Activity is not { } activity || producer.ContextType is not { } contextType)
            return null;

        var discovery = new ActivityDiscovery(producer.Compilation, activity, contextType, report);
        foreach (var attribute in BindingAttributes(producer.Compilation.Assembly))
            discovery.AddAssemblyBinding(attribute);

        // Rebind retained syntax against this compilation, even when only a reference changed.
        foreach (var node in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var model = producer.GetSemanticModel(node.SyntaxTree);
            if (node is TypeDeclarationSyntax declaration && model.GetDeclaredSymbol(declaration, cancellationToken) is INamedTypeSymbol declared)
                discovery.AddDeclaration(declared);
            else if (GetClosedType(model, node, cancellationToken) is { } closed && CanBind(closed, activity))
                discovery.AddInferredBinding(closed, node);
        }

        return new(producer.Compilation, discovery.descriptors, discovery.explicitSites, discovery.inferred,
            discovery.bridgeOwners, discovery.explicitContexts);
    }

    private static IEnumerable<AttributeData> BindingAttributes(ISymbol symbol) =>
        symbol.GetAttributes().Where(attribute => Is(attribute.AttributeClass, Workflows + "ActivityBindingAttribute"));

    private void AddAssemblyBinding(AttributeData attribute)
    {
        if (GetExplicitType(attribute) is not { } type)
        {
            report(Diagnostic.Create(InvalidBinding, AttributeLocation(attribute), compilation.AssemblyName,
                attribute.ConstructorArguments.FirstOrDefault().Value, "explicit activity", "Specify an accessible, concrete, closed IActivity type"));
            return;
        }

        descriptors[type] = false;
        if (AttributeLocation(attribute) is { } site)
            explicitSites[type] = site;
    }

    private void AddDeclaration(INamedTypeSymbol declared)
    {
        if (IsExported(declared) && declared.AllInterfaces.Contains(activity, SymbolEqualityComparer.Default))
        {
            if (declared.IsRefLikeType)
                Report(report, compilation, declared, "boxed activity", "Ref-like activities cannot be boxed as IActivity.");
            else if (declared.TypeKind is TypeKind.Class or TypeKind.Struct)
                bridgeOwners.Add(declared);
        }

        if (CanBind(declared, activity) && compilation.IsSymbolAccessibleWithin(declared, compilation.Assembly) && !HasUnnameableType(declared))
            descriptors[declared] = IsExported(declared);

        if (Extends(declared, contextType))
            foreach (var attribute in BindingAttributes(declared))
                AddContextBinding(declared, attribute);
    }

    private void AddContextBinding(INamedTypeSymbol context, AttributeData attribute)
    {
        var site = AttributeLocation(attribute);
        if (GetExplicitType(attribute) is not { } type)
        {
            Report(report, compilation, context, "explicit activity", "Specify an accessible, concrete, closed IActivity type.",
                site is null ? [] : [site]);
            return;
        }

        if (!explicitContexts.TryGetValue(context, out var rows))
            explicitContexts.Add(context, rows = []);
        if (!rows.Contains(type, SymbolEqualityComparer.Default))
            rows.Add(type);

        inferred.Remove(type);
        if (site is not null)
            explicitSites[type] = site;
        if (!descriptors.ContainsKey(type))
            descriptors.Add(type, false);
    }

    private INamedTypeSymbol? GetExplicitType(AttributeData attribute)
    {
        var argument = attribute.ConstructorArguments.FirstOrDefault().Value as INamedTypeSymbol;
        return argument is not null && CanBind(argument, activity) && !HasUnnameableType(argument) &&
            compilation.IsSymbolAccessibleWithin(argument, compilation.Assembly)
            ? (INamedTypeSymbol)NormalizeBindingType(compilation, argument)
            : null;
    }

    // Factory calls can infer a closed activity without spelling its generic type in source.
    private static INamedTypeSymbol? GetClosedType(SemanticModel model, SyntaxNode node, CancellationToken cancellationToken)
    {
        var type = node switch
        {
            NameSyntax name => model.GetSymbolInfo(name, cancellationToken).Symbol as INamedTypeSymbol,
            InvocationExpressionSyntax invocation => model.GetTypeInfo(invocation, cancellationToken).Type as INamedTypeSymbol,
            _ => null
        };
        return type is { IsGenericType: true } ? type : null;
    }

    private void AddInferredBinding(INamedTypeSymbol closed, SyntaxNode node)
    {
        var canonical = (INamedTypeSymbol)NormalizeBindingType(compilation, closed);
        if (descriptors.ContainsKey(canonical))
            return;

        var accessible = compilation.IsSymbolAccessibleWithin(closed, compilation.Assembly);
        // Report unnameable factory results at the call. An inaccessible generic type reference alone is optional.
        if (!accessible && (node is not InvocationExpressionSyntax ||
            !compilation.IsSymbolAccessibleWithin(closed.OriginalDefinition, compilation.Assembly)))
            return;

        if (!accessible || HasUnnameableType(closed))
        {
            report(Diagnostic.Create(InvalidBinding, node.GetLocation(), compilation.AssemblyName,
                closed.ToDisplayString(), "closed activity type", "Use a named payload type accessible from a separate generated file"));
            return;
        }

        descriptors.Add(canonical, false);
        inferred.Add(canonical);
    }

    private static Location? AttributeLocation(AttributeData attribute) =>
        attribute.ApplicationSyntaxReference is { } syntax ? syntax.SyntaxTree.GetLocation(syntax.Span) : null;

    private static bool Extends(INamedTypeSymbol type, INamedTypeSymbol baseType) =>
        Hierarchy(type).Any(owner => SymbolEqualityComparer.Default.Equals(owner, baseType));

    private static bool CanBind(INamedTypeSymbol type, INamedTypeSymbol activity) =>
        (type.TypeKind is TypeKind.Class or TypeKind.Struct) && !type.IsRefLikeType && !type.IsAbstract && !IsOpen(type) &&
        type.AllInterfaces.Contains(activity, SymbolEqualityComparer.Default);

    private static bool IsOpen(INamedTypeSymbol type) =>
        type.IsUnboundGenericType || type.TypeArguments.Any(IsOpenArgument) ||
        type.ContainingType is { } container && IsOpen(container);

    private static bool IsOpenArgument(ITypeSymbol type) => type is ITypeParameterSymbol ||
        type is INamedTypeSymbol named && IsOpen(named) || type is IArrayTypeSymbol array && IsOpenArgument(array.ElementType);

    // Binding keys follow CLR type identity, which erases dynamic and tuple element names.
    private static ITypeSymbol NormalizeBindingType(Compilation compilation, ITypeSymbol type)
    {
        if (type is IDynamicTypeSymbol)
            return compilation.GetSpecialType(SpecialType.System_Object).WithNullableAnnotation(type.NullableAnnotation);

        var comparer = SymbolEqualityComparer.Default;
        if (type is IArrayTypeSymbol array)
        {
            var element = NormalizeBindingType(compilation, array.ElementType);
            return comparer.Equals(element, array.ElementType)
                ? type
                : compilation.CreateArrayTypeSymbol(element, array.Rank, array.ElementNullableAnnotation).WithNullableAnnotation(type.NullableAnnotation);
        }

        if (type is not INamedTypeSymbol { IsGenericType: true, TypeKind: not TypeKind.Error } named)
            return type;
        named = named.TupleUnderlyingType ?? named;
        var containing = named.ContainingType is { } owner ? (INamedTypeSymbol)NormalizeBindingType(compilation, owner) : null;
        var arguments = named.TypeArguments.Select(argument => NormalizeBindingType(compilation, argument)).ToImmutableArray();
        var sameOwner = comparer.Equals(containing, named.ContainingType);
        if (sameOwner && Enumerable.SequenceEqual(arguments, named.TypeArguments, comparer))
            return named;

        var definition = sameOwner ? named.ConstructedFrom : containing!.GetTypeMembers(named.Name, named.Arity)
            .Single(candidate => comparer.Equals(candidate.OriginalDefinition, named.OriginalDefinition));
        var constructed = arguments.IsEmpty ? definition : definition.Construct(arguments, named.TypeArgumentNullableAnnotations);
        return constructed.WithNullableAnnotation(type.NullableAnnotation);
    }

    private static bool HasUnnameableType(ITypeSymbol type) => type switch
    {
        INamedTypeSymbol named => named.IsAnonymousType || named.IsFileLocal || named.TypeArguments.Any(HasUnnameableType) ||
            named.ContainingType is { } owner && HasUnnameableType(owner),
        IArrayTypeSymbol array => HasUnnameableType(array.ElementType),
        _ => false
    };

    private static bool IsExported(INamedTypeSymbol type) => type.DeclaredAccessibility == Accessibility.Public &&
        (type.ContainingType is null || IsExported(type.ContainingType));
}
