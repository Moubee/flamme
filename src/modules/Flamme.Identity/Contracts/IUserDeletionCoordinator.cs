using Flamme.Identity.Models;

namespace Flamme.Identity.Contracts;

/// <summary>Coordinates guarded user deletion across installed dependency contributors.</summary>
public interface IUserDeletionCoordinator
{
    ValueTask<UserDeletionOperationResult> DeleteAsync(string userId, CancellationToken cancellationToken = default);
}
