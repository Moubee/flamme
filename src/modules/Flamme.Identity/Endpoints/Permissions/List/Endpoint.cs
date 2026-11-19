using Flamme.Abstractions;
using Flamme.Authorization;
using Flamme.Identity.Permissions;
using Flamme.Permissions;
using JetBrains.Annotations;

namespace Flamme.Identity.Endpoints.Permissions.List;

/// <summary>
/// Returns the permission catalog. A role editor renders from this rather than hard-coding permission
/// strings, which is what keeps clients from drifting out of step with the server.
/// </summary>
[PublicAPI]
internal class List(IPermissionDescriptorRegistry registry) : FlammeEndpointWithoutRequest<Response>
{
    /// <inheritdoc />
    public override void Configure()
    {
        Get("/identity/permissions");

        // If you may see roles, you may see what roles can contain.
        RequirePermission(IdentityPermissions.Roles, CoreVerbs.View);
    }

    /// <inheritdoc />
    public override Task<Response> ExecuteAsync(CancellationToken cancellationToken)
    {
        var resources = registry.List()
            .Select(x => new ResourceDescriptor(x.Resource, x.SupportedVerbs, x.NonCoreVerbs, x.DisplayName, x.Description, x.Category, x.Verified))
            .ToArray();

        return Task.FromResult(new Response(Authorization.CoreVerbs.All, resources));
    }
}
