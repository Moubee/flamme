using Flamme.Mediator.Contracts;
using Flamme.Workflows.Runtime.Notifications;

namespace Flamme.Workflows.IntegrationTests.Scenarios.BulkDispatchWithInput;

public class TestHandler(Spy spy) : INotificationHandler<WorkflowDefinitionDispatching>
{
    public Task HandleAsync(WorkflowDefinitionDispatching notification, CancellationToken cancellationToken)
    {
        spy.CaptureDispatch(notification.Request);
        return Task.CompletedTask;
    }
}
