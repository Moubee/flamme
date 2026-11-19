using Flamme.Extensions;
using Flamme.Features.Abstractions;
using Flamme.Features.Services;
using Flamme.Workflows.Activities.Flowchart.Models;
using Flamme.Workflows.Activities.Flowchart.Options;
using Flamme.Workflows.Activities.Flowchart.Serialization;
using Flamme.Workflows.Options;
using Microsoft.Extensions.DependencyInjection;
using Flamme.Common.Serialization;

namespace Flamme.Workflows.Features;

/// <summary>
/// Adds support for the Flowchart activity.
/// </summary>
public class FlowchartFeature : FeatureBase
{
    /// <inheritdoc />
    public FlowchartFeature(IModule module) : base(module)
    {
    }

    /// <summary>
    /// A delegate to configure <see cref="FlowchartOptions"/>.
    /// </summary>
    public Action<FlowchartOptions>? FlowchartOptionsConfigurator { get; set; }

    /// <inheritdoc />
    public override void Apply()
    {
        Services.AddSerializationOptionsConfigurator<FlowchartSerializationOptionConfigurator>();

        // Register FlowchartOptions
        Services.AddOptions<FlowchartOptions>();

        if (FlowchartOptionsConfigurator != null)
            Services.Configure(FlowchartOptionsConfigurator);

        Services.Configure<SerializationTypeOptions>(options => options.AddTypeAlias<FlowScope>("FlowScope"));
    }
}
