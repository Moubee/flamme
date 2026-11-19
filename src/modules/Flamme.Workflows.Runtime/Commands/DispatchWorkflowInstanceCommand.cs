using Flamme.Mediator.Contracts;
using Flamme.Mediator.Models;
using Flamme.Workflows.Models;
using JetBrains.Annotations;

namespace Flamme.Workflows.Runtime.Commands;

/// <summary>
/// A command to dispatch a workflow instance.
/// </summary>
[PublicAPI]
public class DispatchWorkflowInstanceCommand(string instanceId) : ICommand<Unit>
{
    public string InstanceId { get; init; } = instanceId;
    public string? BookmarkId { get; set; }
    public ActivityHandle? ActivityHandle { get; set; }
    public IDictionary<string, object>? Input { get; set; }
    public IDictionary<string, object>? Properties { get; set; }
    public string? CorrelationId { get; set; }
}