using System.Collections.Immutable;
using System.Reflection;

namespace Flamme.Workflows;

/// <summary>
/// Generated bindings and the exported activity inventory of a producer module.
/// </summary>
public abstract class ActivityContext
{
    protected ActivityContext(Assembly assembly, IEnumerable<GeneratedActivityBinding> bindings, IEnumerable<Type> exportedActivityTypes)
    {
        Assembly = assembly;
        Bindings = bindings.ToImmutableArray();
        ExportedActivityTypes = exportedActivityTypes.ToImmutableArray();
    }

    public Assembly Assembly { get; }
    public IReadOnlyList<GeneratedActivityBinding> Bindings { get; }
    public IReadOnlyList<Type> ExportedActivityTypes { get; }
}
