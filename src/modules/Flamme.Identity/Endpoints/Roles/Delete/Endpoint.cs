using Flamme.Authorization;
using Flamme.Abstractions;
using Flamme.Identity.Contracts;
using JetBrains.Annotations;

namespace Flamme.Identity.Endpoints.Roles.Delete;

/// <summary>
/// An endpoint that deletes a role by ID.
/// </summary>
[PublicAPI]
internal class Delete(IRoleDeletionCoordinator coordinator) : FlammeEndpointWithoutRequest
{
    /// <inheritdoc />
    public override void Configure()
    {
        Delete("/identity/roles/{id}");
        RequirePermission(Flamme.Identity.Permissions.IdentityPermissions.Roles, CoreVerbs.Delete);
    }

    /// <inheritdoc />
    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var id = Route<string>("id")!;

        var result = await coordinator.DeleteAsync(id, User, cancellationToken);
        await RoleDeletionEndpointSupport.SendOperationResultAsync(HttpContext, result, cancellationToken);
    }
}
