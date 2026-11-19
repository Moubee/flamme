using Flamme.Authorization;
using Flamme.Abstractions;
using Flamme.Common.Entities;
using Flamme.Models;
using Flamme.Workflows.Runtime;
using Flamme.Workflows.Runtime.Entities;
using Flamme.Workflows.Runtime.Filters;
using Flamme.Workflows.Runtime.OrderDefinitions;
using JetBrains.Annotations;

namespace Flamme.Workflows.Api.Endpoints.ActivityExecutions.List;

/// <summary>
/// Lists the executions for a given activity.
/// </summary>
[PublicAPI]
internal class List : FlammeEndpoint<Request, ListResponse<ActivityExecutionRecord>>
{
    private readonly IActivityExecutionStore _store;

    /// <inheritdoc />
    public List(IActivityExecutionStore store)
    {
        _store = store;
    }

    /// <inheritdoc />
    public override void Configure()
    {
        Get("/activity-executions/list");
        RequirePermission(Flamme.Workflows.Api.Permissions.WorkflowPermissions.ActivityExecutions, CoreVerbs.View);
    }

    /// <inheritdoc />
    public override async Task<ListResponse<ActivityExecutionRecord>> ExecuteAsync(Request request, CancellationToken cancellationToken)
    {
        var filter = new ActivityExecutionRecordFilter
        {
            WorkflowInstanceId = request.WorkflowInstanceId,
            ActivityNodeId = request.ActivityNodeId,
            Completed = request.Completed
        };
        var order = new ActivityExecutionRecordOrder<DateTimeOffset>(x => x.StartedAt, OrderDirection.Ascending);
        var records = (await _store.FindManyAsync(filter, order, cancellationToken)).ToList();
        return new ListResponse<ActivityExecutionRecord>(records);
    }
}