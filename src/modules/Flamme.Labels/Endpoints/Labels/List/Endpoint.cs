using Flamme.Authorization;
using Flamme.Abstractions;
using Flamme.Labels.Contracts;
using FastEndpoints;

namespace Flamme.Labels.Endpoints.Labels.List;

public class List : FlammeEndpoint<Request, Response, PageMapper>
{
    private readonly ILabelStore _store;

    public List(ILabelStore store)
    {
        _store = store;
    }

    public override void Configure()
    {
        Get("/labels");
        RequirePermission(Flamme.Labels.Permissions.LabelPermissions.Labels, CoreVerbs.View);
    }

    public override async Task<Response> ExecuteAsync(Request request, CancellationToken cancellationToken)
    {
        var pageOfLabels = await _store.ListAsync(request.ToPageArgs(), cancellationToken);
        return Map.FromEntity(pageOfLabels);
    }
}