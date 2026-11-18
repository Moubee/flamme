using System.Globalization;
using Microsoft.CodeAnalysis;

namespace Elsa.CodeGeneration.Generators;

/// <summary>A selector can register a concrete type or the type supplied for a method parameter at its call site.</summary>
internal readonly struct SelectorType
{
    internal INamedTypeSymbol? Concrete { get; }
    private ITypeParameterSymbol? SourceParameter { get; }
    private int Ordinal { get; }

    private SelectorType(INamedTypeSymbol? concrete, ITypeParameterSymbol? sourceParameter, int ordinal) =>
        (Concrete, SourceParameter, Ordinal) = (concrete, sourceParameter, ordinal);

    internal SelectorType(INamedTypeSymbol type) : this(type, null, -1) { }
    internal SelectorType(ITypeParameterSymbol parameter) : this(null, parameter, parameter.Ordinal) { }
    internal string ParameterKey => "!!" + Ordinal.ToString(CultureInfo.InvariantCulture);

    internal static bool TryReadParameter(string key, out SelectorType parameter)
    {
        parameter = default;
        if (!key.StartsWith("!!", StringComparison.Ordinal) ||
            !int.TryParse(key.Substring(2), NumberStyles.None, CultureInfo.InvariantCulture, out var ordinal)) return false;
        parameter = new SelectorType(null, null, ordinal);
        return true;
    }

    internal SelectorType Bind(IMethodSymbol method, bool allowOpen, ref string? error)
    {
        if (Concrete != null) return this;
        var definition = (method.ReducedFrom ?? method).OriginalDefinition;
        // A local function can capture the enclosing method's type parameter. Its
        // invocation leaves that parameter for the enclosing method's caller to bind.
        if (SourceParameter != null && !SymbolEqualityComparer.Default.Equals(SourceParameter.DeclaringMethod?.OriginalDefinition, definition))
        {
            if (!allowOpen) error ??= $"feature type parameter '{SourceParameter}' is not fixed at this composition site";
            return this;
        }
        if (Ordinal < method.TypeArguments.Length)
        {
            var argument = method.TypeArguments[Ordinal];
            if (argument is INamedTypeSymbol named && FeatureBinding.IsClosed(named)) return new SelectorType(named);
            if (allowOpen && argument is ITypeParameterSymbol { TypeParameterKind: TypeParameterKind.Method } parameter)
                return new SelectorType(parameter);
        }
        error ??= $"type argument {Ordinal + 1} of '{method}' must be a closed registration type at this composition site";
        return this;
    }
}

internal sealed partial class FixedProducerSelector
{
    internal SelectorType[] SelectedTypes { get; set; } = [];
    internal SelectorType[] ConditionalTypes { get; set; } = [];
    internal SelectorType[] HostedTypes { get; set; } = [];
    internal SelectorType[] FactoryTypes { get; set; } = [];

    internal FixedProducerSelector Bind(IMethodSymbol method, bool allowOpen)
    {
        if (!HasParameters(SelectedTypes) && !HasParameters(ConditionalTypes) && !HasParameters(HostedTypes) && !HasParameters(FactoryTypes))
            return this;

        // Keep definition summaries immutable. Two calls to Select<T> can belong to
        // independent modules, with different T, in the same host compilation.
        var bound = (FixedProducerSelector)MemberwiseClone();
        var error = Error;
        bound.SelectedTypes = Map(SelectedTypes);
        bound.ConditionalTypes = Map(ConditionalTypes);
        bound.HostedTypes = Map(HostedTypes);
        bound.FactoryTypes = Map(FactoryTypes);
        bound.Error = error;
        return bound;

        SelectorType[] Map(SelectorType[] terms) => terms.Select(t => t.Bind(method, allowOpen, ref error)).ToArray();
        static bool HasParameters(SelectorType[] types) => types.Any(t => t.Concrete is null);
    }

    internal static SelectorType[] ConcreteTerms(IEnumerable<INamedTypeSymbol> types) => types.Select(t => new SelectorType(t)).ToArray();
    private static INamedTypeSymbol[] ConcreteTypes(SelectorType[] terms) => terms.Select(t => t.Concrete).OfType<INamedTypeSymbol>()
        .Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default).ToArray();
}
