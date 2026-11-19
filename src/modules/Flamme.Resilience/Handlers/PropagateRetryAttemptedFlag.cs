using Flamme.Mediator.Contracts;
using Flamme.Resilience.Extensions;
using Flamme.Workflows.Runtime.Notifications;
using JetBrains.Annotations;

namespace Flamme.Resilience.Handlers;

[UsedImplicitly]
public class PropagateRetryAttemptedFlag : INotificationHandler<BackgroundActivityExecutionCompleted>
{
    public Task HandleAsync(BackgroundActivityExecutionCompleted notification, CancellationToken cancellationToken)
    {
        var hasRetries = notification.ActivityExecutionContext.GetRetriesAttemptedFlag();

        if (hasRetries)
            notification.ActivityExecutionContext.SetRetriesAttemptedFlag(); // Propagates the flag to all ancestors.

        return Task.CompletedTask;
    }
}