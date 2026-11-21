using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Flamme.ActivityGenerator.Helpers;

internal static class ActivitySymbols
{
    internal const string Workflows = "Flamme.Workflows.";
    internal const string Attributes = Workflows + "Attributes.";
    internal const string ModelsNamespace = Workflows + "Models.";

    internal static Location[] SourceLocations(ISymbol symbol) =>
        symbol.Locations.Where(location => location.IsInSource).ToArray();

    internal static IEnumerable<INamedTypeSymbol> Hierarchy(INamedTypeSymbol type)
    {
        for (var current = type; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
            yield return current;
    }

    internal static bool Is(ITypeSymbol? type, string metadataName) =>
        type is INamedTypeSymbol named && MetadataName(named.OriginalDefinition) == metadataName;

    internal static bool IsAssignableTo(ITypeSymbol type, string metadataName) =>
        Is(type, metadataName) || type is INamedTypeSymbol named && Hierarchy(named).Any(owner => Is(owner, metadataName)) ||
        type.AllInterfaces.Any(iface => Is(iface, metadataName));

    internal static bool Same(ISymbol? first, ISymbol? second) => SymbolEqualityComparer.Default.Equals(first, second);

    internal static IEnumerable<INamedTypeSymbol> ContainingTypes(INamedTypeSymbol type)
    {
        if (type.ContainingType is { } container)
            foreach (var ancestor in ContainingTypes(container))
                yield return ancestor;
        yield return type;
    }

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

    internal static string TypeOf(ITypeSymbol type) => $"typeof({TypeName(type)})";

    internal static string Identifier(string name) => SyntaxFacts.GetKeywordKind(name) == SyntaxKind.None ? name : "@" + name;

    internal static string Quote(string text) => SymbolDisplay.FormatLiteral(text, true);

    internal static string Boolean(bool value) => value ? "true" : "false";
}
