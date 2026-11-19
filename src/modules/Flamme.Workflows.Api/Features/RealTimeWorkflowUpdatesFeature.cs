using Flamme.Features.Abstractions;
using Flamme.Features.Services;
using Flamme.Workflows.Api.RealTime.Handlers;
using Microsoft.Extensions.DependencyInjection;

namespace Flamme.Workflows.Api.Features;

/// <summary>
/// Sets up a SignalR hub for receiving workflow events on the client.
/// </summary>
public class RealTimeWorkflowUpdatesFeature : FeatureBase
{
    /// <inheritdoc />
    public RealTimeWorkflowUpdatesFeature(IModule module) : base(module)
    {
    }

    /// <inheritdoc />
    public override void Apply()
    {
        Services.AddSignalR();
        Services.AddNotificationHandler<BroadcastWorkflowProgress>();
    }
}