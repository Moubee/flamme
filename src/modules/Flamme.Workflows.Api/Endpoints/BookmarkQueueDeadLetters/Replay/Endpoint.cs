using Flamme.Authorization;
using Flamme.Abstractions;
using Flamme.Workflows.Api.Endpoints.BookmarkQueueDeadLetters;
using Flamme.Workflows.Runtime;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Http;

namespace Flamme.Workflows.Api.Endpoints.BookmarkQueueDeadLetters.Replay;

[UsedImplicitly]
internal class Endpoint(IBookmarkQueueDeadLetterManager manager) : FlammeEndpointWithoutRequest<ReplayResponse>
{
    public override void Configure()
    {
        Post("/bookmark-queue/dead-letters/{id}/replay");
        RequirePermission(Flamme.Workflows.Api.Permissions.WorkflowPermissions.BookmarkQueueDeadLetters, "replay");
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var id = Route<string>("id")!;
        var result = await manager.ReplayAsync(id, cancellationToken);

        if (result.Reason == ReplayBookmarkQueueDeadLetterResult.ReasonNotFound)
        {
            await Send.NotFoundAsync(cancellationToken);
            return;
        }

        if (!result.Succeeded)
        {
            await Send.ResponseAsync(new ReplayResponse(false, result.QueueItemId, result.Reason), StatusCodes.Status409Conflict, cancellationToken);
            return;
        }

        await Send.OkAsync(new ReplayResponse(true, result.QueueItemId, null), cancellationToken);
    }
}
