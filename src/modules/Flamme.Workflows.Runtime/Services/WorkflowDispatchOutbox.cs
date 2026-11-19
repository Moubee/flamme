using Flamme.Common;
using Flamme.Workflows.Runtime.Models;

namespace Flamme.Workflows.Runtime;

/// <inheritdoc />
public class WorkflowDispatchOutbox(
    IWorkflowDispatchOutboxStore store,
    IIdentityGenerator identityGenerator,
    TimeProvider timeProvider) : IWorkflowDispatchOutbox
{
    /// <inheritdoc />
    public async Task EnqueueAsync(WorkflowExecutionContext workflowExecutionContext, WorkflowDispatchOutboxItem item, CancellationToken cancellationToken = default)
    {
        item.Id = identityGenerator.GenerateId();
        item.OwnerWorkflowInstanceId = workflowExecutionContext.Id;
        item.CreatedAt = timeProvider.GetUtcNow();

        await store.SaveAsync(item, cancellationToken);
        workflowExecutionContext.AddWorkflowDispatchOutboxItem(item.Id);
    }
}
