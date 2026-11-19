using Flamme.Authorization;
using Flamme.Abstractions;
using Flamme.Secrets.Permissions;
using Flamme.Secrets.Services;

namespace Flamme.Secrets.Endpoints.Secrets.Create;

internal class Endpoint(ISecretManager manager) : FlammeEndpoint<CreateSecretRequest, SecretModel>
{
    public override void Configure()
    {
        Post("/secrets");
        RequirePermission(Flamme.Secrets.Permissions.SecretsResourcePermissions.Secrets, CoreVerbs.Write);
    }

    public override async Task HandleAsync(CreateSecretRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var secret = await manager.CreateAsync(request, cancellationToken);
            await Send.OkAsync(secret.ToModel(), cancellationToken);
        }
        catch (InvalidOperationException e)
        {
            AddError(e.Message);
            await Send.ErrorsAsync(cancellation: cancellationToken);
        }
        catch (ArgumentException e)
        {
            AddError(e.Message);
            await Send.ErrorsAsync(cancellation: cancellationToken);
        }
    }
}
