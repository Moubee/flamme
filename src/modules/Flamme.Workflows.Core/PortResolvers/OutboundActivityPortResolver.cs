using Flamme.Workflows.Models;

namespace Flamme.Workflows.PortResolvers;

/// <summary>
/// Returns a list of outbound activities for a given activity from its generated bindings for public properties matching <see cref="IActivity"/> and <c>ICollection{IActivity}</c>.
/// </summary>
public class PropertyBasedActivityResolver : IActivityResolver
{
    /// <inheritdoc />
    public int Priority => -1;

    /// <inheritdoc />
    public bool GetSupportsActivity(IActivity activity) => true;

    /// <inheritdoc />
    public ValueTask<IEnumerable<ActivityPort>> GetActivityPortsAsync(IActivity activity, CancellationToken cancellationToken = default)
    {
        var binding = ActivityContextRegistry.GetRequiredBinding(activity.GetType());
        return new(binding.GetActivityPorts(activity).ToList());
    }
}
