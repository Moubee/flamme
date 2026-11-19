using Flamme.Testing.Shared.Authorization;
using DashboardApiFeature = Flamme.Dashboard.Api.Features.DashboardApiFeature;

namespace Flamme.Dashboard.Api.UnitTests;

public class EndpointCoverageTests
{
    [Test]
    public void EveryDashboardEndpointDeclaresItsAccess() =>
        EndpointCoverage.AssertEveryEndpointDeclaresAccess(typeof(DashboardApiFeature).Assembly);
}
