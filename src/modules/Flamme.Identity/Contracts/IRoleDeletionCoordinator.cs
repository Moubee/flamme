using System.Security.Claims;
using Flamme.Identity.Models;

namespace Flamme.Identity.Contracts;

/// <summary>Coordinates guarded role deletion across installed dependency contributors.</summary>
public interface IRoleDeletionCoordinator
{
    ValueTask<RoleDeletionInspectionResult> InspectAsync(string roleId, ClaimsPrincipal actor, CancellationToken cancellationToken = default);
    ValueTask<RoleDeletionOperationResult> DeleteAsync(string roleId, ClaimsPrincipal actor, CancellationToken cancellationToken = default);
    ValueTask<RoleDeletionOperationResult> RemediateAndDeleteAsync(RoleDeletionRemediationCommand command, CancellationToken cancellationToken = default);
}
