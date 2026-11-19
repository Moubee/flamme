using Flamme.Authorization;
using Flamme.Abstractions;
using Flamme.Secrets.Permissions;

namespace Flamme.Secrets.Endpoints.Secrets.Delete;

internal class Endpoint(ISecretManager manager) : FlammeEndpointWithoutRequest
{
    public override void Configure()
    {
        Delete("/secrets/{name}");
        RequirePermission(Flamme.Secrets.Permissions.SecretsResourcePermissions.Secrets, CoreVerbs.Delete);
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var deleted = await manager.DeleteAsync(Route<string>("name")!, cancellationToken);
        if (deleted)
            await Send.NoContentAsync(cancellationToken);
        else
            await Send.NotFoundAsync(cancellationToken);
    }
}
