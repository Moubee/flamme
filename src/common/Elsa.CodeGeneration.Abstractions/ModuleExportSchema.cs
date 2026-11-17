namespace Elsa.CodeGeneration;

/// <summary>
/// Identifies the runtime structure of a module export, independently of its contract revision.
/// </summary>
public static class ModuleExportSchema
{
    /// <summary>
    /// The schema accepted by the current runtime registry.
    /// </summary>
    public const int CurrentVersion = 1;
}