using Flamme.Identity.Contracts;
using Flamme.Identity.Entities;
using Flamme.Identity.Models;

namespace Flamme.Identity.UnitTests.Contracts;

public class AccessTokenIssuerContractTests
{
    // A default implementation would let a custom issuer compile without carrying the session over, so that every
    // refresh silently started a new one and signing out left the earlier refresh tokens valid.
    [Test]
    public async Task CustomIssuersMustImplementTheSessionAwareOverload()
    {
        var method = typeof(IAccessTokenIssuer).GetMethod(nameof(IAccessTokenIssuer.IssueTokensAsync), [typeof(User), typeof(SignInSession), typeof(CancellationToken)]);

        await Assert.That(method).IsNotNull();
        await Assert.That(method.IsAbstract).IsTrue();
    }
}
