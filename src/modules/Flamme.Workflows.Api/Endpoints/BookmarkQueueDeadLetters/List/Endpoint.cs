using Flamme.Authorization;
using Flamme.Abstractions;
using Flamme.Common.Entities;
using Flamme.Common.Models;
using Flamme.Workflows.Api.Endpoints.BookmarkQueueDeadLetters;
using Flamme.Workflows.Runtime;
using Flamme.Workflows.Runtime.Filters;
using Flamme.Workflows.Runtime.OrderDefinitions;
using JetBrains.Annotations;

namespace Flamme.Workflows.Api.Endpoints.BookmarkQueueDeadLetters.List;

[UsedImplicitly]
internal class Endpoint(IBookmarkQueueDeadLetterStore store) : FlammeEndpoint<ListRequest, ListResponse>
{
    public override void Configure()
    {
        Verbs(FastEndpoints.Http.GET, FastEndpoints.Http.POST);
        Routes("/bookmark-queue/dead-letters");
        RequirePermission(Flamme.Workflows.Api.Permissions.WorkflowPermissions.BookmarkQueueDeadLetters, CoreVerbs.View);
    }

    public override async Task HandleAsync(ListRequest request, CancellationToken cancellationToken)
    {
        var pageArgs = PageArgs.FromPage(request.Page ?? 0, request.PageSize ?? 50);
        var filter = new BookmarkQueueDeadLetterFilter
        {
            WorkflowInstanceId = request.WorkflowInstanceId
        };
        var order = new BookmarkQueueDeadLetterItemOrder<DateTimeOffset>(x => x.DeadLetteredAt, OrderDirection.Descending);
        var page = await store.PageAsync(pageArgs, filter, order, cancellationToken);
        var response = new ListResponse(page.Items.Select(BookmarkQueueDeadLetterModel.FromEntity).ToList(), page.TotalCount);

        await Send.OkAsync(response, cancellationToken);
    }
}
