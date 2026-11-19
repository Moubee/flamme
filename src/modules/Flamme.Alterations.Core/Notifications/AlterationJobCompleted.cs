using Flamme.Alterations.Core.Entities;
using Flamme.Mediator.Contracts;

namespace Flamme.Alterations.Core.Notifications;

/// <summary>
/// A notification that is published when an alteration job is completed.
/// </summary>
public record AlterationJobCompleted(AlterationJob Job, bool WorkflowContainsScheduledWork) : INotification;