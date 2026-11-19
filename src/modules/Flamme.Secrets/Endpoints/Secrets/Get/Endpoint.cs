using Flamme.Authorization;
using Flamme.Abstractions;
using Flamme.Secrets.Permissions;
using Flamme.Secrets.Services;

namespace Flamme.Secrets.Endpoints.Secrets.Get;

internal class Endpoint(ISecretManager manager) : FlammeEndpointWithoutRequest<SecretModel>
{
    public override void Configure()
    {
        Get("/secrets/{name}");
        RequirePermission(Flamme.Secrets.Permissions.SecretsResourcePermissions.Secrets, CoreVerbs.View);
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var secret = await manager.GetAsync(Route<string>("name")!, cancellationToken);
        if (secret == null)
        {
            await Send.NotFoundAsync(cancellationToken);
            return;
        }

        await Send.OkAsync(secret.ToModel(), cancellationToken);
    }
}
