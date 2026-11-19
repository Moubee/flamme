using Flamme.Authorization;
using Flamme.Abstractions;
using Flamme.Common.Models;
using Flamme.Models;
using Flamme.Resilience.Entities;

namespace Flamme.Resilience.Endpoints.Retries.List;

public class Endpoint(IRetryAttemptReader reader) : FlammeEndpointWithoutRequest
{
    public override void Configure()
    {
        Get("/resilience/retries/{activityInstanceId}");
        RequirePermission(Flamme.Resilience.Permissions.ResiliencePermissions.Retries, CoreVerbs.View);
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var skip = Query<int?>("skip", false);
        var take = Query<int?>("take", false);
        var pageArgs = skip == null && take == null ? null : PageArgs.FromRange(skip, take);
        var activityInstanceId = Route<string>("activityInstanceId");

        if (string.IsNullOrWhiteSpace(activityInstanceId))
        {
            AddError("ActivityInstanceId is required.");
            await Send.ErrorsAsync(cancellation: ct);
            return;
        }

        var page = await reader.ReadAttemptsAsync(activityInstanceId, pageArgs, ct);
        var response = new PagedListResponse<RetryAttemptRecord>(page);
        await Send.OkAsync(response, ct);
    }
}