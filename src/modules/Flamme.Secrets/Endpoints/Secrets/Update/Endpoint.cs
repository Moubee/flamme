using Flamme.Authorization;
using Flamme.Abstractions;
using Flamme.Secrets.Permissions;
using Flamme.Secrets.Services;

namespace Flamme.Secrets.Endpoints.Secrets.Update;

internal class Endpoint(ISecretManager manager) : FlammeEndpoint<UpdateSecretRequest, SecretModel>
{
    public override void Configure()
    {
        Post("/secrets/{name}");
        RequirePermission(Flamme.Secrets.Permissions.SecretsResourcePermissions.Secrets, CoreVerbs.Write);
    }

    public override async Task HandleAsync(UpdateSecretRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var secret = await manager.UpdateAsync(Route<string>("name")!, request, cancellationToken);
            await Send.OkAsync(secret.ToModel(), cancellationToken);
        }
        catch (KeyNotFoundException)
        {
            await Send.NotFoundAsync(cancellationToken);
        }
    }
}
