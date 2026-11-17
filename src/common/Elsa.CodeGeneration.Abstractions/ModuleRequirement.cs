namespace Elsa.CodeGeneration;

/// <summary>
/// Requires one exact module contract revision and selected declared capabilities.
/// </summary>
public sealed class ModuleRequirement
{
    /// <summary>
    /// Copies a required module's identity, positive contract revision, and capability identities.
    /// </summary>
    public ModuleRequirement(string moduleId, int contractVersion, IEnumerable<string>? requiredCapabilities = null)
    {
        ModuleExportValidation.RequireId(moduleId, nameof(moduleId));
        ModuleExportValidation.RequirePositive(contractVersion, nameof(contractVersion));
        ModuleId = moduleId;
        ContractVersion = contractVersion;
        RequiredCapabilities = ModuleExportValidation.SnapshotCapabilities(requiredCapabilities, nameof(requiredCapabilities));
    }

    /// <summary>
    /// The ordinal, case-sensitive identity of the required module.
    /// </summary>
    public string ModuleId { get; }

    /// <summary>
    /// The exact positive contract revision, independent of schema and NuGet package versions.
    /// </summary>
    public int ContractVersion { get; }

    /// <summary>
    /// The immutable capability identities that the required export must declare.
    /// </summary>
    public IReadOnlyList<string> RequiredCapabilities { get; }
}