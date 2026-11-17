using System.Collections.Immutable;

namespace Elsa.CodeGeneration;

/// <summary>
/// Declares a producer's stable module identity and the closed types to include in its generated manifest.
/// Apply to a top-level public static partial class.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ElsaModuleAttribute(string moduleId, int contractVersion, params Type[] declaredTypes) : Attribute
{
    /// <summary>The ordinal module identity used when selecting this export.</summary>
    public string ModuleId { get; } = moduleId;

    /// <summary>The positive revision of the module contract.</summary>
    public int ContractVersion { get; } = contractVersion;

    /// <summary>The types explicitly listed by the producer.</summary>
    public IReadOnlyList<Type> DeclaredTypes { get; } = declaredTypes.ToImmutableArray();
}
