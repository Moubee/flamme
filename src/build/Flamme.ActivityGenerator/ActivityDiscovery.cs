using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Flamme.ActivityGenerator;

internal static class ActivityDiscovery
{
    internal const string Workflows = "Flamme.Workflows.";

    internal static readonly DiagnosticDescriptor InvalidBinding = new("FLAMME4501", "Activity binding cannot be generated",
        "Module '{0}', activity '{1}' is missing binding '{2}'. {3}.", "Flamme", DiagnosticSeverity.Error, true,
        customTags: [WellKnownDiagnosticTags.NotConfigurable]);

    internal sealed record Result(Compilation Compilation, Dictionary<INamedTypeSymbol, bool> Descriptors,
        Dictionary<INamedTypeSymbol, Location> ExplicitSites, HashSet<INamedTypeSymbol> Inferred,
        HashSet<INamedTypeSymbol> BridgeOwners, Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>> ExplicitContexts);

    internal static bool IsCandidate(SyntaxNode node) =>
        node is TypeDeclarationSyntax or GenericNameSyntax or InvocationExpressionSyntax;

    internal static Result? Discover(Compilation compilation, IEnumerable<SyntaxNode> syntax, CancellationToken cancellationToken,
        Action<Diagnostic>? report = null)
    {
        if (compilation.GetTypeByMetadataName(Workflows + "ActivityContext") is null ||
            compilation.GetTypeByMetadataName(Workflows + "IActivity") is null)
            return null;

        // Recover private members and attribute constructors from producer references. Normal
        // compilation binds public API only, including when the reference DLL exposes more metadata.
        compilation = compilation.WithOptions(compilation.Options.WithMetadataImportOptions(MetadataImportOptions.All));
        var activity = compilation.GetTypeByMetadataName(Workflows + "IActivity")!;
        var contextType = compilation.GetTypeByMetadataName(Workflows + "ActivityContext")!;
        var descriptors = new Dictionary<INamedTypeSymbol, bool>(SymbolEqualityComparer.Default);
        var explicitSites = new Dictionary<INamedTypeSymbol, Location>(SymbolEqualityComparer.Default);
        var inferred = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var bridgeOwners = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var attribute in compilation.Assembly.GetAttributes().Where(attribute => Is(attribute.AttributeClass, Workflows + "ActivityBindingAttribute")))
        {
            if (attribute.ConstructorArguments.FirstOrDefault().Value is not INamedTypeSymbol explicitType ||
                !CanBind(explicitType, activity) || HasUnnameableType(explicitType) || !compilation.IsSymbolAccessibleWithin(explicitType, compilation.Assembly))
            {
                var location = attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken).GetLocation();
                report?.Invoke(Diagnostic.Create(InvalidBinding, location, compilation.AssemblyName,
                    attribute.ConstructorArguments.FirstOrDefault().Value, "explicit activity", "Specify an accessible, concrete, closed IActivity type"));
                continue;
            }
            explicitType = (INamedTypeSymbol)NormalizeDynamicType(compilation, explicitType);
            descriptors[explicitType] = false;
            if (AttributeLocation(attribute) is { } site)
                explicitSites[explicitType] = site;
        }
        var explicitContexts = new Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>>(SymbolEqualityComparer.Default);
        foreach (var tree in syntax.GroupBy(node => node.SyntaxTree))
        {
            var semanticModel = compilation.GetSemanticModel(tree.Key);
            foreach (var node in tree)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (node is TypeDeclarationSyntax declaration && semanticModel.GetDeclaredSymbol(declaration) is INamedTypeSymbol declared)
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

                    if (!Extends(declared, contextType))
                        continue;
                    foreach (var attribute in declared.GetAttributes().Where(attribute => Is(attribute.AttributeClass, Workflows + "ActivityBindingAttribute")))
                    {
                        if (attribute.ConstructorArguments.FirstOrDefault().Value is not INamedTypeSymbol explicitType ||
                            !CanBind(explicitType, activity) || HasUnnameableType(explicitType) || !compilation.IsSymbolAccessibleWithin(explicitType, compilation.Assembly))
                        {
                            var attributeSite = AttributeLocation(attribute);
                            Report(report, compilation, declared, "explicit activity", "Specify an accessible, concrete, closed IActivity type.",
                                attributeSite is null ? [] : [attributeSite]);
                            continue;
                        }

                        explicitType = (INamedTypeSymbol)NormalizeDynamicType(compilation, explicitType);
                        if (!explicitContexts.TryGetValue(declared, out var explicitRows))
                            explicitContexts.Add(declared, explicitRows = []);
                        if (!explicitRows.Contains(explicitType, SymbolEqualityComparer.Default))
                            explicitRows.Add(explicitType);
                        inferred.Remove(explicitType);
                        if (AttributeLocation(attribute) is { } site)
                            explicitSites[explicitType] = site;
                        if (!descriptors.ContainsKey(explicitType))
                            descriptors.Add(explicitType, false);
                    }
                }
                else
                {
                    // Factory calls can infer a closed activity without spelling its generic type in source.
                    var closed = node switch
                    {
                        GenericNameSyntax generic => semanticModel.GetSymbolInfo(generic, cancellationToken).Symbol as INamedTypeSymbol,
                        InvocationExpressionSyntax invocation => semanticModel.GetTypeInfo(invocation, cancellationToken).Type as INamedTypeSymbol,
                        _ => null
                    };
                    if (closed is { IsGenericType: true } && CanBind(closed, activity))
                    {
                        var canonical = (INamedTypeSymbol)NormalizeDynamicType(compilation, closed);
                        if (descriptors.ContainsKey(canonical))
                            continue;

                        var accessible = compilation.IsSymbolAccessibleWithin(closed, compilation.Assembly);
                        // Diagnose payloads at factory calls while leaving inaccessible activity declarations and type references optional.
                        if (!accessible && (node is not InvocationExpressionSyntax ||
                            !compilation.IsSymbolAccessibleWithin(closed.OriginalDefinition, compilation.Assembly)))
                            continue;

                        if (!accessible || HasUnnameableType(closed))
                        {
                            report?.Invoke(Diagnostic.Create(InvalidBinding, node.GetLocation(), compilation.AssemblyName,
                                closed.ToDisplayString(), "closed activity type", "Use a named payload type accessible from a separate generated file"));
                            continue;
                        }
                        descriptors.Add(canonical, false);
                        inferred.Add(canonical);
                    }
                }
            }
        }

        return new(compilation, descriptors, explicitSites, inferred, bridgeOwners, explicitContexts);
    }

    internal static void Report(Action<Diagnostic>? report, Compilation compilation, INamedTypeSymbol activity,
        string binding, string message, Location[]? sites = null, Location? request = null)
    {
        var primary = sites?.FirstOrDefault() ?? activity.Locations.FirstOrDefault(location => location.IsInSource) ?? request;
        report?.Invoke(Diagnostic.Create(InvalidBinding, primary, sites?.Skip(1), properties: null,
            messageArgs: [compilation.AssemblyName, TypeName(activity), binding, message.TrimEnd('.')]));
    }

    private static Location? AttributeLocation(AttributeData attribute) =>
        attribute.ApplicationSyntaxReference is { } syntax ? syntax.SyntaxTree.GetLocation(syntax.Span) : null;

    private static bool CanBind(INamedTypeSymbol type, INamedTypeSymbol activity) =>
        (type.TypeKind is TypeKind.Class or TypeKind.Struct) && !type.IsRefLikeType && !type.IsAbstract && !IsOpen(type) &&
        type.AllInterfaces.Contains(activity, SymbolEqualityComparer.Default);

    private static bool IsOpen(INamedTypeSymbol type) =>
        type.IsUnboundGenericType || type.TypeArguments.Any(IsOpenArgument) ||
        type.ContainingType is { } container && IsOpen(container);

    private static bool IsOpenArgument(ITypeSymbol type) => type is ITypeParameterSymbol ||
        type is INamedTypeSymbol named && IsOpen(named) || type is IArrayTypeSymbol array && IsOpenArgument(array.ElementType);

    // Roslyn distinguishes dynamic from object in symbol keys. Both spellings name the same CLR row.
    private static ITypeSymbol NormalizeDynamicType(Compilation compilation, ITypeSymbol type)
    {
        if (type is IDynamicTypeSymbol)
            return compilation.GetSpecialType(SpecialType.System_Object).WithNullableAnnotation(type.NullableAnnotation);

        var comparer = SymbolEqualityComparer.Default;
        if (type is IArrayTypeSymbol array)
        {
            var element = NormalizeDynamicType(compilation, array.ElementType);
            return comparer.Equals(element, array.ElementType)
                ? type
                : compilation.CreateArrayTypeSymbol(element, array.Rank, array.ElementNullableAnnotation).WithNullableAnnotation(type.NullableAnnotation);
        }

        if (type is not INamedTypeSymbol { IsGenericType: true, TypeKind: not TypeKind.Error } named)
            return type;
        if (named.IsTupleType)
        {
            var elements = named.TupleElements.Select(element => NormalizeDynamicType(compilation, element.Type)).ToImmutableArray();
            if (Enumerable.SequenceEqual(elements, named.TupleElements.Select(element => element.Type), comparer))
                return type;

            return compilation.CreateTupleTypeSymbol(elements,
                named.TupleElements.Select(element => element.IsExplicitlyNamedTupleElement ? element.Name : null).ToImmutableArray(),
                elementNullableAnnotations: named.TupleElements.Select(element => element.NullableAnnotation).ToImmutableArray())
                .WithNullableAnnotation(type.NullableAnnotation);
        }

        var containing = named.ContainingType is { } owner ? (INamedTypeSymbol)NormalizeDynamicType(compilation, owner) : null;
        var arguments = named.TypeArguments.Select(argument => NormalizeDynamicType(compilation, argument)).ToImmutableArray();
        var sameOwner = comparer.Equals(containing, named.ContainingType);
        if (sameOwner && Enumerable.SequenceEqual(arguments, named.TypeArguments, comparer))
            return type;

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

    internal static IEnumerable<INamedTypeSymbol> Hierarchy(INamedTypeSymbol type)
    {
        for (var current = type; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
            yield return current;
    }

    private static bool Extends(INamedTypeSymbol type, INamedTypeSymbol baseType) =>
        Hierarchy(type).Any(owner => SymbolEqualityComparer.Default.Equals(owner, baseType));

    internal static bool Is(ITypeSymbol? type, string metadataName) =>
        type is INamedTypeSymbol named && MetadataName(named.OriginalDefinition) == metadataName;

    internal static string MetadataName(INamedTypeSymbol type) => type.ContainingType is { } container
        ? MetadataName(container) + "+" + type.MetadataName
        : type.ContainingNamespace.IsGlobalNamespace ? type.MetadataName : type.ContainingNamespace + "." + type.MetadataName;

    internal static string TypeName(ITypeSymbol type)
    {
        var format = SymbolDisplayFormat.FullyQualifiedFormat;
        var name = type.ToDisplayString(format);
        if (name.IndexOf("dynamic", StringComparison.Ordinal) < 0)
            return name;

        return string.Concat(type.ToDisplayParts(format)
            .Select(part => part.Symbol is IDynamicTypeSymbol ? "object" : part.ToString()));
    }
}
