using Flamme.Authorization;
using Flamme.Abstractions;
using Flamme.Labels.Contracts;
using Flamme.Workflows;
using FastEndpoints;
using JetBrains.Annotations;

namespace Flamme.Labels.Endpoints.Labels.Post;

[PublicAPI]
internal class Create : FlammeEndpoint<Request, Response, LabelMapper>
{
    private readonly ILabelStore _store;

    public Create(ILabelStore store, IIdentityGenerator identityGenerator)
    {
        _store = store;
    }

    public override void Configure()
    {
        Post("/labels");
        RequirePermission(Flamme.Labels.Permissions.LabelPermissions.Labels, CoreVerbs.Create);
    }

    public override async Task HandleAsync(Request request, CancellationToken cancellationToken)
    {
        var label = Map.ToEntity(request);
        await _store.SaveAsync(label, cancellationToken);
        var response = Map.FromEntity(label);
        await Send.CreatedAtAsync<Get.Get>(new { Id = label.Id }, response, cancellation: cancellationToken);
    }
}