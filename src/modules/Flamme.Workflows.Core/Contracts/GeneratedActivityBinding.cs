using System.Collections.Frozen;
using System.Collections.Immutable;
using Flamme.Workflows.Models;

namespace Flamme.Workflows;

/// <summary>
/// A producer's direct operations and catalog factory for one exact CLR activity type.
/// </summary>
public sealed class GeneratedActivityBinding
{
    private ImmutableArray<ActivityMemberBinding> _inputs;
    private ImmutableArray<ActivityMemberBinding> _outputs;
    private Lazy<IReadOnlySet<string>>? _publicPropertyNames;

    public required Type ActivityType { get; init; }
    public required string Name { get; init; }
    public string? Namespace { get; init; }
    public required string TypeName { get; init; }
    public required int Version { get; init; }

    /// <summary>
    /// Identifies an automatically inferred closed generic specialization.
    /// </summary>
    public bool IsInferred { get; init; }

    /// <summary>
    /// Creates a fresh descriptor using the host's default-value resolver.
    /// </summary>
    public required Func<IPropertyDefaultValueResolver, ActivityDescriptor> CreateDescriptor { get; init; }

    /// <summary>
    /// Activates an activity that also supplies property defaults using host-scoped services.
    /// </summary>
    public Func<IServiceProvider, IActivityPropertyDefaultValueProvider>? CreateDefaultValueProvider { get; init; }

    public required IReadOnlyList<ActivityMemberBinding> Inputs
    {
        get => _inputs;
        init => _inputs = [.. value];
    }

    public required IReadOnlyList<ActivityMemberBinding> Outputs
    {
        get => _outputs;
        init => _outputs = [.. value];
    }

    /// <summary>
    /// Supplies the names of the activity's public CLR properties.
    /// </summary>
    public required Func<IEnumerable<string>> PublicPropertyNamesFactory
    {
        init => _publicPropertyNames = value is null ? null : new(() => MaterializePublicPropertyNames(value));
    }

    /// <summary>
    /// The immutable public CLR property names, compared using ordinal spelling.
    /// </summary>
    public IReadOnlySet<string> PublicPropertyNames => _publicPropertyNames!.Value;

    public required Func<string, string> GetOutcomeName { get; init; }
    public required Func<IActivity, IEnumerable<ActivityPort>> GetActivityPorts { get; init; }
    public required Func<IActivity, string, ExecuteActivityDelegate?> BindBookmarkCallback { get; init; }
    public required Func<IActivity, string, ActivityCompletionCallback?> BindCompletionCallback { get; init; }

    internal void ValidateRuntimeFactories()
    {
        if (_publicPropertyNames is null)
            throw ActivityContextRegistry.MissingBinding(ActivityType, "public property name factory");
    }

    private IReadOnlySet<string> MaterializePublicPropertyNames(Func<IEnumerable<string>> factory) =>
        factory()?.ToFrozenSet(StringComparer.Ordinal)
        ?? throw ActivityContextRegistry.MissingBinding(ActivityType, "public property names");
}
