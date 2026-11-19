using Flamme.Identity.Models;

namespace Flamme.Identity.Contracts;

/// <summary>
/// Creates Flamme access and local refresh tokens from trusted issuance data.
/// </summary>
public interface IFlammeTokenService
{
    /// <summary>
    /// Issues a Flamme access token.
    /// </summary>
    ValueTask<IssuedAccessToken> IssueAccessTokenAsync(TokenIssuanceContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Issues a local Flamme refresh token.
    /// </summary>
    ValueTask<IssuedAccessToken> IssueRefreshTokenAsync(TokenIssuanceContext context, CancellationToken cancellationToken = default);
}
