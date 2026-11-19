using Flamme.Authorization;
using Flamme.Abstractions;
using Flamme.Secrets.Permissions;
using Flamme.Secrets.Services;

namespace Flamme.Secrets.Endpoints.Secrets.Rotate;

internal class Endpoint(ISecretManager manager) : FlammeEndpoint<RotateSecretRequest, SecretModel>
{
    public override void Configure()
    {
        Post("/secrets/{name}/rotate");
        RequirePermission(Flamme.Secrets.Permissions.SecretsResourcePermissions.Secrets, CoreVerbs.Write);
    }

    public override async Task HandleAsync(RotateSecretRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var secret = await manager.RotateAsync(Route<string>("name")!, request, cancellationToken);
            await Send.OkAsync(secret.ToModel(), cancellationToken);
        }
        catch (InvalidOperationException e)
        {
            AddError(e.Message);
            await Send.ErrorsAsync(cancellation: cancellationToken);
        }
        catch (KeyNotFoundException)
        {
            await Send.NotFoundAsync(cancellationToken);
        }
    }
}
