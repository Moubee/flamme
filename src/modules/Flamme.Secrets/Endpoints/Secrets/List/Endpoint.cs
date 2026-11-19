using Flamme.Authorization;
using Flamme.Abstractions;
using Flamme.Secrets.Permissions;
using Flamme.Secrets.Services;

namespace Flamme.Secrets.Endpoints.Secrets.List;

internal class Endpoint(ISecretManager manager) : FlammeEndpoint<ListSecretsRequest, ListSecretsResponse>
{
    public override void Configure()
    {
        Get("/secrets");
        RequirePermission(Flamme.Secrets.Permissions.SecretsResourcePermissions.Secrets, CoreVerbs.View);
    }

    public override async Task<ListSecretsResponse> ExecuteAsync(ListSecretsRequest request, CancellationToken cancellationToken)
    {
        var result = await manager.ListPageAsync(request, cancellationToken);
        var models = result.Items.Select(x => x.ToModel()).ToList();
        return new ListSecretsResponse { Items = models, TotalCount = result.TotalCount };
    }
}
