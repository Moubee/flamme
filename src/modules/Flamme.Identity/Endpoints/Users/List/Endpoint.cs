using Flamme.Common.Multitenancy;
using Flamme.Authorization;
using Flamme.Abstractions;
using Flamme.Identity.Contracts;
using Flamme.Identity.Models;
using JetBrains.Annotations;

namespace Flamme.Identity.Endpoints.Users.List;

/// <summary>
/// An endpoint that lists all users.
/// </summary>
[PublicAPI]
internal class List(IUserStore userStore, ITenantAccessor tenantAccessor) : FlammeEndpointWithoutRequest<Response>
{
    /// <inheritdoc />
    public override void Configure()
    {
        Get("/identity/users");
        RequirePermission(Flamme.Identity.Permissions.IdentityPermissions.Users, CoreVerbs.View);
    }

    /// <inheritdoc />
    public override async Task<Response> ExecuteAsync(CancellationToken cancellationToken)
    {
        var users = await userStore.FindManyAsync(new() { TenantId = tenantAccessor.TenantId }, cancellationToken);

        var response = new Response(users
            .Select(user => new UserSummary(user.Id, user.Name, user.Roles, user.TenantId))
            .ToList());

        return response;
    }
}

