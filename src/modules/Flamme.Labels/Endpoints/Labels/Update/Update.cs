using Flamme.Authorization;
using Flamme.Abstractions;
using Flamme.Labels.Contracts;
using FastEndpoints;
using JetBrains.Annotations;

namespace Flamme.Labels.Endpoints.Labels.Update;

[PublicAPI]
internal class Update(ILabelStore store) : FlammeEndpoint<Request, Response, LabelMapper>
{
    public override void Configure()
    {
        Post("/labels/{id}");
        RequirePermission(Flamme.Labels.Permissions.LabelPermissions.Labels, CoreVerbs.Update);
    }

    public override async Task HandleAsync(Request request, CancellationToken cancellationToken)
    {
        var label = await store.FindByIdAsync(request.Id, cancellationToken);

        if (label == null)
        {
            await Send.NotFoundAsync(cancellationToken);
            return;
        }

        label = Map.UpdateEntity(request, label);

        await store.SaveAsync(label, cancellationToken);
        var response = Map.FromEntity(label);
        await Send.OkAsync(response, cancellationToken);
    }
}