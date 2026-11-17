namespace Elsa.CodeGeneration;

/// <summary>
/// Explicitly selects typed exports without discovery, global registration, or activation side effects.
/// </summary>
public sealed class ModuleExportRegistryBuilder<TExport> where TExport : ModuleExport
{
    private readonly List<TExport> _exports = [];
    private readonly Dictionary<string, int> _positions = new(StringComparer.Ordinal);

    /// <summary>
    /// Selects an export. Readding the current object is idempotent; a distinct object with the same module ID is a conflict.
    /// </summary>
    public ModuleExportRegistryBuilder<TExport> Add(TExport export)
    {
        ArgumentNullException.ThrowIfNull(export);
        var id = export.Descriptor.ModuleId;
        if (_positions.TryGetValue(id, out var position))
        {
            if (ReferenceEquals(_exports[position], export))
                return this;
            throw new InvalidOperationException($"Module export '{id}' already selects a different export object at contract revision {_exports[position].Descriptor.ContractVersion}; use an explicit replacement.");
        }
        _positions.Add(id, _exports.Count);
        _exports.Add(export);
        return this;
    }

    /// <summary>
    /// Replaces the exact currently selected object with an export of the same logical module ID, retaining its first-add slot.
    /// </summary>
    public ModuleExportRegistryBuilder<TExport> Replace(TExport expected, TExport replacement)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(replacement);
        var id = expected.Descriptor.ModuleId;
        if (!string.Equals(id, replacement.Descriptor.ModuleId, StringComparison.Ordinal))
            throw new InvalidOperationException($"Cannot replace module export '{id}' with a different module ID '{replacement.Descriptor.ModuleId}'.");
        if (!_positions.TryGetValue(id, out var position) || !ReferenceEquals(_exports[position], expected))
            throw new InvalidOperationException($"Cannot replace module export '{id}': the expected export object is not currently selected.");
        _exports[position] = replacement;
        return this;
    }

    /// <summary>
    /// Validates the final selection and snapshots dependency-first exports using first-add order among ready modules.
    /// </summary>
    public ModuleExportRegistry<TExport> Build()
    {
        var snapshot = _exports.ToArray();
        var selected = snapshot.ToDictionary(export => export.Descriptor.ModuleId, StringComparer.Ordinal);
        ValidateGraph(snapshot, selected);

        var indegrees = snapshot.Select(export => export.Descriptor.Requirements.Count).ToArray();
        var dependents = selected.Keys.ToDictionary(id => id, _ => new List<int>(), StringComparer.Ordinal);
        for (var index = 0; index < snapshot.Length; index++)
        {
            foreach (var requirement in snapshot[index].Descriptor.Requirements)
                dependents[requirement.ModuleId].Add(index);
        }
        var ready = new PriorityQueue<int, int>();
        for (var index = 0; index < snapshot.Length; index++)
        {
            if (indegrees[index] == 0)
                ready.Enqueue(index, index);
        }
        var ordered = new List<TExport>(snapshot.Length);
        while (ready.TryDequeue(out var index, out _))
        {
            var export = snapshot[index];
            ordered.Add(export);
            foreach (var dependent in dependents[export.Descriptor.ModuleId])
            {
                if (--indegrees[dependent] == 0)
                    ready.Enqueue(dependent, dependent);
            }
        }
        return new(ordered);
    }

    private static void ValidateGraph(IReadOnlyList<TExport> exports, IReadOnlyDictionary<string, TExport> selected)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var active = new HashSet<string>(StringComparer.Ordinal);
        var path = new List<string>();
        var capabilities = exports.ToDictionary(export => export.Descriptor.ModuleId,
            export => export.Descriptor.Capabilities.ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);
        foreach (var export in exports)
            Visit(export);
        return;

        void Visit(TExport export)
        {
            var descriptor = export.Descriptor;
            var id = descriptor.ModuleId;
            if (active.Contains(id))
                throw new InvalidOperationException($"Module export dependency cycle: {Format(path.Skip(path.IndexOf(id)).Append(id))}.");
            if (!visited.Add(id))
                return;
            active.Add(id);
            path.Add(id);
            if (descriptor.SchemaVersion != ModuleExportSchema.CurrentVersion)
                throw new InvalidOperationException($"Module export '{id}' uses schema {descriptor.SchemaVersion}; supported schema is {ModuleExportSchema.CurrentVersion}. Dependency path: {Format(path)}.");
            foreach (var requirement in descriptor.Requirements)
            {
                if (!selected.TryGetValue(requirement.ModuleId, out var dependency))
                    throw new InvalidOperationException($"Missing required module export '{requirement.ModuleId}'. Dependency path: {Format(path.Append(requirement.ModuleId))}.");
                if (dependency.Descriptor.ContractVersion != requirement.ContractVersion)
                    throw new InvalidOperationException($"Module export '{id}' requires '{requirement.ModuleId}' at exact contract revision {requirement.ContractVersion}, but selected revision is {dependency.Descriptor.ContractVersion}. Dependency path: {Format(path.Append(requirement.ModuleId))}.");
                foreach (var capability in requirement.RequiredCapabilities)
                {
                    if (!capabilities[requirement.ModuleId].Contains(capability))
                        throw new InvalidOperationException($"Module export '{id}' requires capability '{capability}' from '{requirement.ModuleId}'. Dependency path: {Format(path.Append(requirement.ModuleId))}.");
                }
                Visit(dependency);
            }
            path.RemoveAt(path.Count - 1);
            active.Remove(id);
        }
    }

    private static string Format(IEnumerable<string> path) => string.Join(" -> ", path);
}