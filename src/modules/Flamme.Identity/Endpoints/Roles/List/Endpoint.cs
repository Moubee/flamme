using Flamme.Common.Multitenancy;
using Flamme.Authorization;
using Flamme.Abstractions;
using Flamme.Identity.Contracts;
using JetBrains.Annotations;

namespace Flamme.Identity.Endpoints.Roles.List;

/// <summary>
/// An endpoint that lists all roles.
/// </summary>
[PublicAPI]
internal class List(IRoleStore roleStore, ITenantAccessor tenantAccessor) : FlammeEndpointWithoutRequest<Response>
{
    /// <inheritdoc />
    public override void Configure()
    {
        Get("/identity/roles");
        RequirePermission(Flamme.Identity.Permissions.IdentityPermissions.Roles, CoreVerbs.View);
    }

    /// <inheritdoc />
    public override async Task<Response> ExecuteAsync(CancellationToken cancellationToken)
    {
        var roles = await roleStore.FindManyAsync(new() { TenantId = tenantAccessor.TenantId }, cancellationToken);

        var response = new Response(roles
            .Select(role => new RoleSummary(role.Id, role.Name, role.Permissions, role.TenantId))
            .ToList());

        return response;
    }
}