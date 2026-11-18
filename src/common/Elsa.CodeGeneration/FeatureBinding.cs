using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Elsa.CodeGeneration.Generators;

/// <summary>Projects the managed feature provider's conventions onto closed symbols in the producer.</summary>
internal sealed class FeatureBinding(Compilation compilation, INamedTypeSymbol feature)
{
    private readonly INamedTypeSymbol? _dependsOn = compilation.GetTypeByMetadataName("Elsa.Features.Attributes.DependsOnAttribute");
    private readonly INamedTypeSymbol? _dependencyOf = compilation.GetTypeByMetadataName("Elsa.Features.Attributes.DependencyOfAttribute");
    private readonly INamedTypeSymbol? _displayName = compilation.GetTypeByMetadataName("System.ComponentModel.DisplayNameAttribute");
    private readonly INamedTypeSymbol? _description = compilation.GetTypeByMetadataName("System.ComponentModel.DescriptionAttribute");
    private readonly INamedTypeSymbol? _module = compilation.GetTypeByMetadataName("Elsa.Features.Implementations.Module");
    private readonly INamedTypeSymbol? _hostedService = compilation.GetTypeByMetadataName("Microsoft.Extensions.Hosting.IHostedService");

    internal bool IsFeature(INamedTypeSymbol type) => type.AllInterfaces.Contains(feature, SymbolEqualityComparer.Default);
    internal static string TypeName(ITypeSymbol type) => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    internal static bool IsClosed(ITypeSymbol type) => type switch
    {
        ITypeParameterSymbol => false,
        INamedTypeSymbol named => !named.IsUnboundGenericType && named.TypeArguments.All(IsClosed) &&
                                 (named.ContainingType is null || IsClosed(named.ContainingType)),
        IArrayTypeSymbol array => IsClosed(array.ElementType),
        _ => true
    };

    // File-local types (including type arguments) cannot be named from a generated file.
    private bool CanName(ITypeSymbol type, IAssemblySymbol within) => compilation.IsSymbolAccessibleWithin(type, within) && type switch
    {
        INamedTypeSymbol named => !named.IsFileLocal && named.TypeArguments.All(arg => CanName(arg, within)) &&
                                 (named.ContainingType is null || CanName(named.ContainingType, within)),
        IArrayTypeSymbol array => CanName(array.ElementType, within),
        _ => true
    };

    internal string? HostedServiceError(INamedTypeSymbol type) =>
        type.TypeKind == TypeKind.Class && !type.IsAbstract && _hostedService is not null && type.AllInterfaces.Contains(_hostedService, SymbolEqualityComparer.Default) && CanName(type, compilation.Assembly)
            ? null : "use an accessible, closed, concrete hosted-service implementation";

    internal string? FeatureError(INamedTypeSymbol type, IAssemblySymbol within)
    {
        if (type.TypeKind != TypeKind.Class || type.IsAbstract || !IsFeature(type) || !CanName(type, within))
            return "use an accessible, closed, concrete feature implementation";

        foreach (var attribute in InheritedAttributes(type))
        {
            // Derived attributes can execute arbitrary code to compute their Type or
            // display text. Compiling their constructor arguments as literals changes it.
            for (var parent = attribute.AttributeClass?.BaseType; parent is not null; parent = parent.BaseType)
                if (Same(parent, _dependsOn) || Same(parent, _dependencyOf) || Same(parent, _displayName) || Same(parent, _description))
                    return $"metadata from derived attribute '{attribute.AttributeClass}' cannot be evaluated at build time";

            if (!Same(attribute.AttributeClass, _dependsOn) && !Same(attribute.AttributeClass, _dependencyOf))
                continue;
            if (DependencyType(attribute) is not INamedTypeSymbol dependency || !CanName(dependency, within) || !IsClosed(dependency))
                return $"dependency in '{attribute}' must be an accessible, closed feature type";
        }
        return null;
    }

    internal (INamedTypeSymbol[] Strong, INamedTypeSymbol[] Conditional) Dependencies(INamedTypeSymbol type)
    {
        var strong = new List<INamedTypeSymbol>();
        var conditional = new List<INamedTypeSymbol>();
        foreach (var attribute in InheritedAttributes(type))
        {
            var list = Same(attribute.AttributeClass, _dependsOn) ? strong :
                Same(attribute.AttributeClass, _dependencyOf) ? conditional : null;
            if (list is null)
                continue;

            if (DependencyType(attribute) is INamedTypeSymbol dependency)
                list.Add(dependency);
        }
        return (strong.ToArray(), conditional.ToArray());
    }

    private static ITypeSymbol? DependencyType(AttributeData attribute)
    {
        var argument = attribute.NamedArguments.FirstOrDefault(pair => pair.Key == "Type");
        var value = argument.Key is null ? attribute.ConstructorArguments.FirstOrDefault() : argument.Value;
        return value.Value as ITypeSymbol;
    }

    internal string Registration(INamedTypeSymbol type, INamedTypeSymbol[] strong, INamedTypeSymbol[] conditional)
    {
        var name = type.MetadataName.Replace("Feature", string.Empty);
        var displayName = ReadStringAttribute(type, _displayName) ?? name;
        var description = ReadStringAttribute(type, _description) ?? string.Empty;
        var metadata = $"new global::Elsa.Features.Models.FeatureDescriptor({Literal(name)}, \"Elsa\", {Literal(displayName)}, {Literal(description)})";
        return $"target.AddAvailable<{TypeName(type)}>({Factory(type)}, {metadata}, {TypeArray(strong)}, {TypeArray(conditional)});";
    }

    private string Factory(INamedTypeSymbol type)
    {
        // Activator receives the concrete Module. Select the most specific applicable public
        // parameter, even if an overload accepts object or IModule. A custom factory can still
        // supply a feature with no usable default constructor.
        if (_module is null)
            return "null";
        var constructors = type.InstanceConstructors
            .Where(ctor => ctor.DeclaredAccessibility == Accessibility.Public && ctor.Parameters.Length == 1 &&
                           ctor.Parameters[0].RefKind == RefKind.None && Assignable(_module, ctor.Parameters[0].Type))
            .ToArray();
        var best = constructors.FirstOrDefault(ctor => constructors.All(other => Assignable(ctor.Parameters[0].Type, other.Parameters[0].Type)));
        if (best is null || HasUnsatisfiedRequiredMembers(best))
            return "null";
        return $"static module => new {TypeName(type)}(({TypeName(best.Parameters[0].Type)})module)";
    }

    private static bool HasUnsatisfiedRequiredMembers(IMethodSymbol ctor)
    {
        if (ctor.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() == "System.Diagnostics.CodeAnalysis.SetsRequiredMembersAttribute"))
            return false;
        for (var type = ctor.ContainingType; type is not null; type = type.BaseType)
            if (type.GetMembers().Any(member => member is IPropertySymbol { IsRequired: true } or IFieldSymbol { IsRequired: true }))
                return true;
        return false;
    }

    private bool Assignable(ITypeSymbol from, ITypeSymbol to)
    {
        var conversion = compilation.ClassifyCommonConversion(from, to);
        return conversion.IsIdentity || conversion.IsImplicit && conversion.IsReference;
    }

    private static string TypeArray(IReadOnlyCollection<INamedTypeSymbol> types) => types.Count == 0 ? "null" :
        "new global::System.Type[] { " + string.Join(", ", types.Select(type => $"typeof({TypeName(type)})")) + " }";

    private static string Literal(string value) => SymbolDisplay.FormatLiteral(value, quote: true);

    private static string? ReadStringAttribute(INamedTypeSymbol type, INamedTypeSymbol? attributeType)
    {
        var attribute = InheritedAttributes(type).FirstOrDefault(item => Same(item.AttributeClass, attributeType));
        if (attribute is null)
            return null;
        return attribute.ConstructorArguments.Length == 0 ? string.Empty : attribute.ConstructorArguments[0].Value as string;
    }

    private static IEnumerable<AttributeData> InheritedAttributes(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
            foreach (var attribute in current.GetAttributes())
                yield return attribute;
    }

    private static bool Same(ISymbol? symbol, ISymbol? other) => symbol is not null && SymbolEqualityComparer.Default.Equals(symbol, other);
}
