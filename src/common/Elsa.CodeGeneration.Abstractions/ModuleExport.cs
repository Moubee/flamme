namespace Elsa.CodeGeneration;

/// <summary>
/// Base contract for an immutable typed export whose bindings remain in their owning runtime assembly.
/// </summary>
public abstract class ModuleExport
{
    /// <summary>
    /// Creates an export with immutable descriptor facts. Derived exports must retain immutable binding declarations.
    /// </summary>
    protected ModuleExport(ModuleExportDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        Descriptor = descriptor;
    }

    /// <summary>
    /// The immutable export identity and dependency facts.
    /// </summary>
    public ModuleExportDescriptor Descriptor { get; }
}