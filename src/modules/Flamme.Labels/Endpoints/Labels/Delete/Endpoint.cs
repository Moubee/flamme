using Flamme.Authorization;
using Flamme.Abstractions;
using Flamme.Labels.Contracts;
using FastEndpoints;

namespace Flamme.Labels.Endpoints.Labels.Delete;

public class Delete : FlammeEndpoint<Request>
{
    private readonly ILabelStore _store;

    public Delete(ILabelStore store) => _store = store;

    public override void Configure()
    {
        Delete("/labels/{id}");
        RequirePermission(Flamme.Labels.Permissions.LabelPermissions.Labels, CoreVerbs.Delete);
    }

    public override async Task HandleAsync(Request request, CancellationToken cancellationToken)
    {
        var deleted = await _store.DeleteAsync(request.Id, cancellationToken);

        if (deleted)
            await Send.NoContentAsync(cancellationToken);
        else
            await Send.NotFoundAsync(cancellationToken);
    }
}