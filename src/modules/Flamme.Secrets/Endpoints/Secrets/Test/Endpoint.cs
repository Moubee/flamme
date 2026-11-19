using Flamme.Authorization;
using Flamme.Abstractions;
using Flamme.Secrets.Permissions;

namespace Flamme.Secrets.Endpoints.Secrets.Test;

internal class Endpoint(ISecretManager manager) : FlammeEndpointWithoutRequest<SecretTestResponse>
{
    public override void Configure()
    {
        Post("/secrets/{name}/test");
        RequirePermission(Flamme.Secrets.Permissions.SecretsResourcePermissions.Secrets, "test");
    }

    public override Task<SecretTestResponse> ExecuteAsync(CancellationToken cancellationToken)
    {
        return manager.TestAsync(Route<string>("name")!, cancellationToken);
    }
}
