using Elsa.Workflows.Management.Entities;
using Elsa.Workflows.Runtime.Handlers;
using Elsa.Workflows.Runtime.Models;
using Elsa.Workflows.Runtime.Notifications;
using Elsa.Workflows.Runtime.Options;
using Elsa.Workflows.State;
using Microsoft.Extensions.Logging.Abstractions;
using TUnit.Mocks;

namespace Elsa.Workflows.Runtime.UnitTests.Handlers;

public class ProcessWorkflowDispatchOutboxTests
{
    private readonly IWorkflowDispatchOutboxProcessor _processor = Mock.Of<IWorkflowDispatchOutboxProcessor>().Object;
    private readonly WorkflowDispatcherOptions _options = new()
    {
        UseTransactionalOutbox = true,
        ProcessOutboxAfterCommit = true
    };
    private readonly IServiceProvider _serviceProvider = Mock.Of<IServiceProvider>().Object;

    public ProcessWorkflowDispatchOutboxTests()
    {
        Mock.Get(_serviceProvider).GetService(typeof(IWorkflowDispatchOutboxProcessor)).Returns(_processor);
    }

    [Test]
    public async Task HandleAsync_DoesNotProcess_WhenCommittedStateHasNoOutboxItems()
    {
        var handler = CreateHandler();
        var notification = CreateNotification(new WorkflowState());

        await handler.HandleAsync(notification, CancellationToken.None);

        Mock.Get(_processor).TryProcessAsync(Arg.Any<CancellationToken>()).WasNeverCalled();
    }

    [Test]
    public async Task HandleAsync_Processes_WhenCommittedStateHasOutboxItems()
    {
        var handler = CreateHandler();
        var notification = CreateNotification(new WorkflowState
        {
            Properties = new Dictionary<string, object>
            {
                [WorkflowDispatchOutboxStateExtensions.PropertyKey] = new WorkflowDispatchOutboxState
                {
                    ItemIds = ["outbox-1"]
                }
            }
        });

        await handler.HandleAsync(notification, CancellationToken.None);

        Mock.Get(_processor).TryProcessAsync(CancellationToken.None).WasCalled(Times.Exactly(1));
    }

    [Test]
    public async Task HandleAsync_DoesNotThrow_WhenEagerOutboxProcessingFails()
    {
        var handler = CreateHandler();
        var notification = CreateNotification(new WorkflowState
        {
            Properties = new Dictionary<string, object>
            {
                [WorkflowDispatchOutboxStateExtensions.PropertyKey] = new WorkflowDispatchOutboxState
                {
                    ItemIds = ["outbox-1"]
                }
            }
        });
        Mock.Get(_processor).TryProcessAsync(Arg.Any<CancellationToken>()).Returns(() => Task.FromException<bool>(new InvalidOperationException("Store unavailable.")));

        await handler.HandleAsync(notification, CancellationToken.None);
    }

    private ProcessWorkflowDispatchOutbox CreateHandler() => new(_serviceProvider, Microsoft.Extensions.Options.Options.Create(_options), NullLogger<ProcessWorkflowDispatchOutbox>.Instance);

    private static WorkflowStateCommitted CreateNotification(WorkflowState workflowState)
    {
        return new(null!, workflowState, new WorkflowInstance());
    }
}
