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

    internal static SelectorType? From(ITypeSymbol? type) => type switch
    {
        INamedTypeSymbol named when FeatureBinding.IsClosed(named) => new SelectorType(named),
        ITypeParameterSymbol { TypeParameterKind: TypeParameterKind.Method } parameter => new SelectorType(parameter),
        _ => null
    };

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
        var argument = ResolveParameter(method, allowOpen, ref error);
        if (From(argument) is {} bound) return bound;
        error ??= $"type argument {Ordinal + 1} of '{method}' must be a closed registration type at this composition site";
        return this;
    }

    internal ITypeSymbol? ResolveParameter(IMethodSymbol method, bool allowOpen, ref string? error)
    {
        var definition = (method.ReducedFrom ?? method).OriginalDefinition;
        // A local function can capture the enclosing method's type parameter. Its
        // invocation leaves that parameter for the enclosing method's caller to bind.
        if (SourceParameter != null && !SymbolEqualityComparer.Default.Equals(SourceParameter.DeclaringMethod?.OriginalDefinition, definition))
        {
            if (!allowOpen) error ??= $"feature type parameter '{SourceParameter}' is not fixed at this composition site";
            return SourceParameter;
        }
        if (Ordinal < method.TypeArguments.Length)
        {
            var argument = method.TypeArguments[Ordinal];
            if (FeatureBinding.IsClosed(argument) || allowOpen && argument is ITypeParameterSymbol { TypeParameterKind: TypeParameterKind.Method })
                return argument;
        }
        error ??= $"type argument {Ordinal + 1} of '{method}' must be a closed registration type at this composition site";
        return null;
    }
}

// A selector resolves one slot, invokes a host callback, or applies every slot.
internal readonly struct SlotResolution(SelectorType? type, bool conditional = false, bool factory = false, string? callback = null)
{
    internal SelectorType? Type { get; } = type;
    internal bool Conditional { get; } = conditional;
    internal bool Factory { get; } = factory;
    internal string? Callback { get; } = callback;
}

// Slot types must be named registrations. A virtual method can also receive an array.
internal readonly struct VirtualArgument(ITypeSymbol? concrete, SelectorType parameter = default)
{
    internal ITypeSymbol? Concrete => concrete;
    internal static VirtualArgument? From(ITypeSymbol? type) => type switch
    {
        INamedTypeSymbol or IArrayTypeSymbol when FeatureBinding.IsClosed(type) => new VirtualArgument(type),
        ITypeParameterSymbol { TypeParameterKind: TypeParameterKind.Method } p => new VirtualArgument(null, new SelectorType(p)),
        _ => null
    };

    internal VirtualArgument Bind(IMethodSymbol method, bool allowOpen, ref string? error) => concrete != null ? this :
        From(parameter.ResolveParameter(method, allowOpen, ref error)) ?? this;

    internal static string AssemblyName(ITypeSymbol type)
    {
        while (type is IArrayTypeSymbol array) type = array.ElementType;
        return type.ContainingAssembly?.Identity.Name ?? "";
    }

    internal string Key => concrete == null ? parameter.ParameterKey : AssemblyName(concrete) + "|" + DocumentationCommentId.CreateReferenceId(concrete);
}

/// <summary>Virtual dispatch is resolved on the admitted feature type, using the arguments from the original call.</summary>
internal sealed class VirtualCall(string methodKey, VirtualArgument[] arguments, string? receiverParameter = null)
{
    internal string MethodKey => methodKey;
    internal string? ReceiverParameter => receiverParameter;
    internal VirtualArgument[] Arguments => arguments;
    internal VirtualCall Bind(IMethodSymbol method, bool allowOpen, ref string? error)
    {
        if (arguments.All(argument => argument.Concrete != null)) return this;
        var bound = new VirtualArgument[arguments.Length];
        for (var i = 0; i < bound.Length; i++)
            bound[i] = arguments[i].Bind(method, allowOpen, ref error);
        return new VirtualCall(methodKey, bound, receiverParameter);
    }

    // Documentation IDs contain no newlines. Non-generic calls retain their original metadata.
    internal string Encode() => string.Join("\n", new[] { receiverParameter == null ? methodKey : "@" + receiverParameter + "\n" + methodKey }.Concat(arguments.Select(argument => argument.Key)));
}

internal sealed partial class FixedProducerSelector
{
    internal SelectorType[] SelectedTypes { get; set; } = [];
    internal SelectorType[] ConditionalTypes { get; set; } = [];
    internal SelectorType[] HostedTypes { get; set; } = [];
    internal SlotResolution[] Resolutions { get; set; } = [];

    internal IEnumerable<SlotResolution> OrderedResolutions() => Resolutions.Length > 0 ? Resolutions :
        SelectedTypes.Select(type => new SlotResolution(type))
            .Concat(ConditionalTypes.Select(type => new SlotResolution(type, conditional: true)))
            .Concat(ConfigurationParameters.Select(name => new SlotResolution(null, callback: name)))
            .Concat(ConditionalConfigurationParameters.Select(name => new SlotResolution(null, conditional: true, callback: name)));

    internal FixedProducerSelector Bind(IMethodSymbol method, bool allowOpen)
    {
        if (!HasParameters(SelectedTypes) && !HasParameters(ConditionalTypes) && !HasParameters(HostedTypes) &&
            !VirtualCalls.Concat(ConditionalVirtualCalls).Any(call => call.Arguments.Any(arg => arg.Concrete == null)))
            return this;

        // Keep definition summaries immutable. Two calls to Select<T> can belong to
        // independent modules, with different T, in the same host compilation.
        var bound = (FixedProducerSelector)MemberwiseClone();
        var error = Error;
        bound.SelectedTypes = Map(SelectedTypes);
        bound.ConditionalTypes = Map(ConditionalTypes);
        bound.HostedTypes = Map(HostedTypes);
        bound.Resolutions = Resolutions.Select(step =>
            new SlotResolution(step.Type?.Bind(method, allowOpen, ref error), step.Conditional, step.Factory, step.Callback)).ToArray();
        bound.VirtualCalls = VirtualCalls.Select(call => call.Bind(method, allowOpen, ref error)).ToArray();
        bound.ConditionalVirtualCalls = ConditionalVirtualCalls.Select(call => call.Bind(method, allowOpen, ref error)).ToArray();
        bound.Error = error;
        return bound;

        SelectorType[] Map(SelectorType[] terms) => terms.Select(t => t.Bind(method, allowOpen, ref error)).ToArray();
        static bool HasParameters(SelectorType[] types) => types.Any(t => t.Concrete is null);
    }

    internal static SelectorType[] ConcreteTerms(IEnumerable<INamedTypeSymbol> types) => types.Select(t => new SelectorType(t)).ToArray();
    private static INamedTypeSymbol[] ConcreteTypes(SelectorType[] terms) => terms.Select(t => t.Concrete).OfType<INamedTypeSymbol>()
        .Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default).ToArray();
}
