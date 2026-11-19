using Flamme.Identity.Models;

namespace Flamme.Identity.Contracts;

/// <summary>
/// Validates and exchanges Flamme identity refresh tokens.
/// </summary>
public interface IIdentityRefreshTokenService
{
    /// <summary>
    /// Validates the specified refresh token and issues a new token pair using the user's current roles and permissions.
    /// </summary>
    ValueTask<IssuedTokens?> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default);
}
