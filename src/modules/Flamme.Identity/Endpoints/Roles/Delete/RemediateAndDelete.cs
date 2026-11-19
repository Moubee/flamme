using Flamme.Authorization;
using Flamme.Abstractions;
using Flamme.Identity.Contracts;
using Flamme.Identity.Models;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Http;

namespace Flamme.Identity.Endpoints.Roles.Delete;

/// <summary>Removes a role from all editable dependency policies and deletes it only after reinspection succeeds.</summary>
[PublicAPI]
internal sealed class RemediateAndDelete(IRoleDeletionCoordinator coordinator) : FlammeEndpoint<RemediateRoleDeletionRequest>
{
    public override void Configure()
    {
        Post("/identity/roles/{id}/remove-from-jit-policies-and-delete");
        RequirePermission(Flamme.Identity.Permissions.IdentityPermissions.Roles, CoreVerbs.Delete);
    }

    public override async Task HandleAsync(RemediateRoleDeletionRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ExpectedDependencyVersion))
        {
            HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            await HttpContext.Response.WriteAsJsonAsync(
                new RoleDeletionErrorResponse("validation_failed", "The expected dependency version is required."),
                cancellationToken);
            return;
        }

        var result = await coordinator.RemediateAndDeleteAsync(
            new RoleDeletionRemediationCommand(
                Route<string>("id")!,
                User,
                request.ExpectedDependencyVersion,
                request.ConfirmRemoveFromEditableJitPolicies,
                request.ConfirmEmptyDefaultRoles,
                request.ConfirmBestEffort)
            {
                SelectedReferences = request.SelectedReferences,
                ReplacementRoleId = request.ReplacementRoleId
            },
            cancellationToken);
        await RoleDeletionEndpointSupport.SendOperationResultAsync(HttpContext, result, cancellationToken);
    }
}
