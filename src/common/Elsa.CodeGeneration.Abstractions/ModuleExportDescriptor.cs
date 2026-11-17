using System.Collections.Immutable;

namespace Elsa.CodeGeneration;

/// <summary>
/// Immutable identity, schema, contract, and dependency facts for an explicitly selected module export.
/// </summary>
public sealed class ModuleExportDescriptor
{
    /// <summary>
    /// Snapshots the authored requirements and capabilities. Unsupported positive schemas are rejected by registry Build.
    /// </summary>
    public ModuleExportDescriptor(string moduleId, int contractVersion, IEnumerable<ModuleRequirement>? requirements = null,
        IEnumerable<string>? capabilities = null, int schemaVersion = ModuleExportSchema.CurrentVersion)
    {
        ModuleExportValidation.RequireId(moduleId, nameof(moduleId));
        ModuleExportValidation.RequirePositive(contractVersion, nameof(contractVersion));
        ModuleExportValidation.RequirePositive(schemaVersion, nameof(schemaVersion));
        ModuleId = moduleId;
        ContractVersion = contractVersion;
        SchemaVersion = schemaVersion;
        var snapshot = requirements?.ToImmutableArray() ?? [];
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var requirement in snapshot)
        {
            if (requirement is null)
                throw new ArgumentException("A module requirement must not be null.", nameof(requirements));
            if (!identities.Add(requirement.ModuleId))
                throw new ArgumentException($"Duplicate requirement for module '{requirement.ModuleId}'.", nameof(requirements));
        }
        Requirements = snapshot;
        Capabilities = ModuleExportValidation.SnapshotCapabilities(capabilities, nameof(capabilities));
    }

    /// <summary>
    /// The ordinal, case-sensitive identity of the exporting module.
    /// </summary>
    public string ModuleId { get; }

    /// <summary>
    /// The exact positive contract revision, independent of schema and NuGet package versions.
    /// </summary>
    public int ContractVersion { get; }

    /// <summary>
    /// The positive export-structure schema, validated by registry Build.
    /// </summary>
    public int SchemaVersion { get; }

    /// <summary>
    /// The immutable required module facts in authored order.
    /// </summary>
    public IReadOnlyList<ModuleRequirement> Requirements { get; }

    /// <summary>
    /// The immutable capability identities declared by this export.
    /// </summary>
    public IReadOnlyList<string> Capabilities { get; }
}