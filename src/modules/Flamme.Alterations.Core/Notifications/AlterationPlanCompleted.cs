using Flamme.Alterations.Core.Entities;
using Flamme.Mediator.Contracts;

namespace Flamme.Alterations.Core.Notifications;

/// <summary>
/// A notification that is published when an alteration plan is completed.
/// </summary>
public record AlterationPlanCompleted(AlterationPlan Plan) : INotification;