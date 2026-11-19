using Flamme.Authorization;
using Flamme.Abstractions;
using Flamme.Secrets.Permissions;
using Flamme.Secrets.Services;

namespace Flamme.Secrets.Endpoints.Secrets.Revoke;

internal class Endpoint(ISecretManager manager) : FlammeEndpointWithoutRequest<SecretModel>
{
    public override void Configure()
    {
        Post("/secrets/{name}/revoke");
        RequirePermission(Flamme.Secrets.Permissions.SecretsResourcePermissions.Secrets, CoreVerbs.Write);
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var name = Route<string>("name")!;
        var secret = await manager.RevokeAsync(name, cancellationToken);
        if (secret == null)
        {
            await Send.NotFoundAsync(cancellationToken);
            return;
        }

        await Send.OkAsync(secret.ToModel(), cancellationToken);
    }
}
