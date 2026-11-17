using System.Collections.Immutable;

namespace Elsa.CodeGeneration;

/// <summary>
/// Reports the CLR types explicitly declared by one producer. Runtime bindings belong to their owning modules.
/// </summary>
public sealed class ModuleTypeManifest : ModuleExport
{
    /// <summary>Snapshots the producer's closed types in declaration-table order.</summary>
    public ModuleTypeManifest(ModuleExportDescriptor descriptor, IEnumerable<Type> declaredTypes) : base(descriptor)
    {
        ArgumentNullException.ThrowIfNull(declaredTypes);
        var snapshot = declaredTypes.ToImmutableArray();
        var seen = new HashSet<Type>();
        foreach (var type in snapshot)
        {
            if (type is null || type.ContainsGenericParameters || type.HasElementType || type.IsFunctionPointer || type == typeof(void))
                throw new ArgumentException("Declare a closed named type for each manifest entry.", nameof(declaredTypes));
            if (!seen.Add(type))
                throw new ArgumentException($"Type '{type}' is declared more than once in module '{descriptor.ModuleId}'.", nameof(declaredTypes));
        }
        DeclaredTypes = snapshot;
    }

    /// <summary>The immutable coverage table for the declared CLR types.</summary>
    public IReadOnlyList<Type> DeclaredTypes { get; }
}
