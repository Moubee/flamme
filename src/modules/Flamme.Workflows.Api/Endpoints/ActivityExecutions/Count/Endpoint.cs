using Flamme.Authorization;
using Flamme.Abstractions;
using Flamme.Models;
using Flamme.Workflows.Runtime;
using Flamme.Workflows.Runtime.Filters;
using JetBrains.Annotations;

namespace Flamme.Workflows.Api.Endpoints.ActivityExecutions.Count;

/// <summary>
/// Counts the number of executions for a given activity.
/// </summary>
[PublicAPI]
internal class Count : FlammeEndpoint<Request, CountResponse>
{
    private readonly IActivityExecutionStore _store;

    /// <inheritdoc />
    public Count(IActivityExecutionStore store)
    {
        _store = store;
    }

    /// <inheritdoc />
    public override void Configure()
    {
        Get("/activity-executions/count");
        RequirePermission(Flamme.Workflows.Api.Permissions.WorkflowPermissions.ActivityExecutions, CoreVerbs.View);
    }

    /// <inheritdoc />
    public override async Task<CountResponse> ExecuteAsync(Request request, CancellationToken cancellationToken)
    {
        var filter = new ActivityExecutionRecordFilter
        {
            WorkflowInstanceId = request.WorkflowInstanceId,
            ActivityId = request.ActivityId,
            Completed = request.Completed
        };
        var count = await _store.CountAsync(filter, cancellationToken);
        return new CountResponse(count);
    }
}