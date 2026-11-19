using Flamme.Alterations.Activities;

namespace Flamme.Alterations.Bookmarks;

/// <summary>
/// Represents bookmark payload for the <see cref="AlterationPlanCompleted"/> trigger.
/// </summary>
/// <param name="PlanId">The ID of the alteration plan.</param>
public record AlterationPlanCompletedPayload(string PlanId);