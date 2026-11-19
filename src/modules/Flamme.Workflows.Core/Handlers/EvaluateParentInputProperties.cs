using Flamme.Extensions;
using Flamme.Mediator.Contracts;
using Flamme.Workflows.Notifications;

namespace Flamme.Workflows.Handlers;

public class EvaluateParentInputProperties : INotificationHandler<InvokingActivityCallback>
{
    public async Task HandleAsync(InvokingActivityCallback notification, CancellationToken cancellationToken)
    {
        // Before invoking the parent activity, make sure its properties are evaluated.
        if (!notification.Parent.GetHasEvaluatedProperties()) 
            await notification.Parent.EvaluateInputPropertiesAsync();
    }
}