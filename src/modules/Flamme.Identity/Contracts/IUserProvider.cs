using Flamme.Identity.Entities;
using Flamme.Identity.Models;

namespace Flamme.Identity.Contracts;

/// <summary>
/// Represents a provider of users.
/// </summary>
public interface IUserProvider
{
    /// <summary>
    /// Finds a user matching the specified filter.
    /// </summary>
    /// <param name="filter">The filter to use.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The user if found, otherwise <c>null</c>.</returns>
    Task<User?> FindAsync(UserFilter filter, CancellationToken cancellationToken = default);
}