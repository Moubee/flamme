using System.Collections.Immutable;

namespace Elsa.CodeGeneration;

/// <summary>
/// A host-local snapshot of selected typed exports, ordered before any capability-specific binding is applied.
/// </summary>
public sealed class ModuleExportRegistry<TExport> where TExport : ModuleExport
{
    internal ModuleExportRegistry(IEnumerable<TExport> exports) => Exports = exports.ToImmutableArray();

    /// <summary>
    /// Exports in dependency-first order; simultaneously ready exports retain their original first-add order.
    /// </summary>
    public IReadOnlyList<TExport> Exports { get; }
}