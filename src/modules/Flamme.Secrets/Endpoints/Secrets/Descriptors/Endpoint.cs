using Flamme.Authorization;
using Flamme.Abstractions;
using Flamme.Secrets.Permissions;

namespace Flamme.Secrets.Endpoints.Secrets.Descriptors;

internal class Endpoint(ISecretTypeRegistry typeRegistry, ISecretStoreRegistry storeRegistry) : FlammeEndpointWithoutRequest<SecretDescriptorsResponse>
{
    public override void Configure()
    {
        Get("/secrets/descriptors");
        RequirePermission(Flamme.Secrets.Permissions.SecretsResourcePermissions.Secrets, CoreVerbs.View);
    }

    public override Task<SecretDescriptorsResponse> ExecuteAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(new SecretDescriptorsResponse
        {
            Types = typeRegistry.List().ToList(),
            Stores = storeRegistry.List().Select(x => x.Descriptor).ToList()
        });
    }
}
