using Flamme.Authorization;
using Flamme.Abstractions;
using Flamme.Workflows.Runtime;
using Flamme.Workflows.Runtime.Entities;
using Flamme.Workflows.Runtime.Filters;
using JetBrains.Annotations;

namespace Flamme.Workflows.Api.Endpoints.ActivityExecutions.Get;

/// <summary>
/// Gets an individual execution for a given activity.
/// </summary>
[PublicAPI]
internal class Endpoint(IActivityExecutionStore store) : FlammeEndpointWithoutRequest<ActivityExecutionRecord>
{
    /// <inheritdoc />
    public override void Configure()
    {
        Get("/activity-executions/{id}", "/activity-executions/{*id}", "/activity-executions/by-id");
        RequirePermission(Flamme.Workflows.Api.Permissions.WorkflowPermissions.ActivityExecutions, CoreVerbs.View);
    }

    /// <inheritdoc />
    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var id = Query<string>("id", false) ?? Route<string>("id", false);

        if (string.IsNullOrWhiteSpace(id))
        {
            AddError("The activity execution ID is required.");
            await Send.ErrorsAsync(cancellation: cancellationToken);
            return;
        }

        var filter = new ActivityExecutionRecordFilter
        {
            Id = id
        };

        var record = await store.FindAsync(filter, cancellationToken);

        if (record == null)
        {
            await Send.NotFoundAsync(cancellationToken);
            return;
        }

        await Send.OkAsync(record, cancellationToken);
    }
}
