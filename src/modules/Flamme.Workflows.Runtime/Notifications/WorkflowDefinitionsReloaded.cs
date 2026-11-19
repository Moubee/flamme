using Flamme.Mediator.Contracts;
using Flamme.Workflows.Runtime.Models;

namespace Flamme.Workflows.Runtime.Notifications;

/// <summary>
/// Published when workflow definitions have been reloaded.
/// </summary>
public record WorkflowDefinitionsReloaded(ICollection<ReloadedWorkflowDefinition> ReloadedWorkflowDefinitions) : INotification;