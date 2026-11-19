using Flamme.Mediator;
using Flamme.Mediator.Contracts;
using Flamme.Workflows.Runtime.Notifications;

namespace Flamme.Workflows.Runtime;

/// <summary>
/// Relies on the <see cref="INotificationSender"/> to publish the received request as a domain event from a background worker.
/// </summary>
public class BackgroundTaskDispatcher(INotificationSender notificationSender) : ITaskDispatcher
{
    /// <inheritdoc />
    public async Task DispatchAsync(RunTaskRequest request, CancellationToken cancellationToken = default)
    {
        await notificationSender.SendAsync(request, NotificationStrategy.Background, cancellationToken);
    }
}