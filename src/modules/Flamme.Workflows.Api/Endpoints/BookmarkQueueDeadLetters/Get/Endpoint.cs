using Flamme.Authorization;
using Flamme.Abstractions;
using Flamme.Workflows.Api.Endpoints.BookmarkQueueDeadLetters;
using Flamme.Workflows.Runtime;
using Flamme.Workflows.Runtime.Filters;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Http;

namespace Flamme.Workflows.Api.Endpoints.BookmarkQueueDeadLetters.Get;

[UsedImplicitly]
internal class Endpoint(IBookmarkQueueDeadLetterStore store) : FlammeEndpointWithoutRequest<BookmarkQueueDeadLetterModel>
{
    public override void Configure()
    {
        Get("/bookmark-queue/dead-letters/{id}");
        RequirePermission(Flamme.Workflows.Api.Permissions.WorkflowPermissions.BookmarkQueueDeadLetters, CoreVerbs.View);
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var id = Route<string>("id")!;
        var item = await store.FindAsync(new BookmarkQueueDeadLetterFilter { Id = id }, cancellationToken);

        if (item == null)
        {
            await Send.NotFoundAsync(cancellationToken);
            return;
        }

        await Send.OkAsync(BookmarkQueueDeadLetterModel.FromEntity(item), cancellationToken);
    }
}
