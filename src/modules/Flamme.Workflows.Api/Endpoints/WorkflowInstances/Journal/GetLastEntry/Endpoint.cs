using Flamme.Authorization;
using Flamme.Abstractions;
using Flamme.Common.Entities;
using Flamme.Workflows.Runtime;
using Flamme.Workflows.Runtime.Entities;
using Flamme.Workflows.Runtime.Filters;
using Flamme.Workflows.Runtime.OrderDefinitions;
using JetBrains.Annotations;

namespace Flamme.Workflows.Api.Endpoints.WorkflowInstances.Journal.GetLastEntry;

/// <summary>
/// Return the last log entry for the specified workflow instance and activity ID.
/// </summary>
[PublicAPI]
public class Get(IWorkflowExecutionLogStore store) : FlammeEndpoint<Request, WorkflowExecutionLogRecord>
{
    /// <inheritdoc />
    public override void Configure()
    {
        Get("/workflow-instances/{workflowInstanceId}/journal/{activityId}");
        RequirePermission(Flamme.Workflows.Api.Permissions.WorkflowPermissions.Instances, CoreVerbs.View);
    }

    /// <inheritdoc />
    public override async Task HandleAsync(Request request, CancellationToken cancellationToken)
    {
        var filter = new WorkflowExecutionLogRecordFilter
        {
            WorkflowInstanceId = request.WorkflowInstanceId,
            ActivityId = request.ActivityId,
            EventNames = ["Started", "Completed", "Faulted"]
        };

        // Sequence is the instance-monotonic event cursor. Same-timestamp batches must not
        // pick an arbitrary "last" entry when only Timestamp descending is applied.
        var sort = new WorkflowExecutionLogRecordOrder<long>(
            x => x.Sequence,
            OrderDirection.Descending
        );

        var entry = await store.FindAsync(filter, sort, cancellationToken);

        if (entry == null)
        {
            await Send.NotFoundAsync(cancellationToken);
            return;
        }

        await Send.OkAsync(entry, cancellationToken);
    }
}