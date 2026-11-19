using Flamme.Mediator.Contracts;
using Flamme.UserTasks.Contracts;
using Flamme.UserTasks.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Flamme.UserTasks.Services;

public sealed class DefaultUserTaskNotificationSink(IServiceProvider services) : IUserTaskNotificationSink
{
    public Task PublishAsync(UserTaskLifecycleNotification notification, CancellationToken cancellationToken = default)
    {
        return services.GetService<INotificationSender>()?.SendAsync(notification, cancellationToken) ?? Task.CompletedTask;
    }
}
