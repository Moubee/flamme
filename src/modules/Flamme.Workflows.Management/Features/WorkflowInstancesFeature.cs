using Flamme.Features.Abstractions;
using Flamme.Features.Services;
using Flamme.Workflows.Management.Stores;
using Microsoft.Extensions.DependencyInjection;

namespace Flamme.Workflows.Management.Features;

/// <summary>
/// Enables storage of workflow instances.
/// </summary>
public class WorkflowInstancesFeature(IModule module) : FeatureBase(module)
{
    /// <summary>
    /// The factory to create new instances of <see cref="IWorkflowInstanceStore"/>.
    /// </summary>
    public Func<IServiceProvider, IWorkflowInstanceStore> WorkflowInstanceStore { get; set; } = sp => sp.GetRequiredService<MemoryWorkflowInstanceStore>();

    /// <inheritdoc />
    public override void Apply()
    {
        Services.AddScoped(WorkflowInstanceStore);
    }
}