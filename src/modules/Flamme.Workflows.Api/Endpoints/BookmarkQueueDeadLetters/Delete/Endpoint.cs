using Flamme.Authorization;
using Flamme.Abstractions;
using Flamme.Workflows.Runtime;
using Flamme.Workflows.Runtime.Filters;
using JetBrains.Annotations;

namespace Flamme.Workflows.Api.Endpoints.BookmarkQueueDeadLetters.Delete;

[UsedImplicitly]
internal class Endpoint(IBookmarkQueueDeadLetterStore store) : FlammeEndpointWithoutRequest
{
    public override void Configure()
    {
        Delete("/bookmark-queue/dead-letters/{id}");
        RequirePermission(Flamme.Workflows.Api.Permissions.WorkflowPermissions.BookmarkQueueDeadLetters, CoreVerbs.Delete);
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var id = Route<string>("id")!;
        var count = await store.DeleteAsync(new BookmarkQueueDeadLetterFilter { Id = id }, cancellationToken);

        if (count == 0)
        {
            await Send.NotFoundAsync(cancellationToken);
            return;
        }

        await Send.NoContentAsync(cancellationToken);
    }
}
