using Flamme.Identity.Services;
using Flamme.Testing.Shared.Authorization;

namespace Flamme.Identity.UnitTests.Authorization;

public class EndpointCoverageTests
{
    [Test]
    public void EveryIdentityEndpointDeclaresItsAccess() =>
        EndpointCoverage.AssertEveryEndpointDeclaresAccess(typeof(RoleAuthorizationService).Assembly);
}