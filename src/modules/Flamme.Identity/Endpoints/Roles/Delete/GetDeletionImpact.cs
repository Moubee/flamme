using Flamme.Authorization;
using Flamme.Abstractions;
using Flamme.Identity.Contracts;
using JetBrains.Annotations;

namespace Flamme.Identity.Endpoints.Roles.Delete;

/// <summary>Returns the current cross-module impact of deleting a role.</summary>
[PublicAPI]
internal sealed class GetDeletionImpact(IRoleDeletionCoordinator coordinator) : FlammeEndpointWithoutRequest
{
    public override void Configure()
    {
        Get("/identity/roles/{id}/deletion-impact");
        RequirePermission(Flamme.Identity.Permissions.IdentityPermissions.Roles, CoreVerbs.Delete);
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var result = await coordinator.InspectAsync(Route<string>("id")!, User, cancellationToken);
        await RoleDeletionEndpointSupport.SendInspectionResultAsync(HttpContext, result, cancellationToken);
    }
}
