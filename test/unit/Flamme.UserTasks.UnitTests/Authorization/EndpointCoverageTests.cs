using Flamme.Testing.Shared.Authorization;
using Flamme.UserTasks.Services;

namespace Flamme.UserTasks.UnitTests.Authorization;

public class EndpointCoverageTests
{
    [Test]
    public void EveryUserTasksEndpointDeclaresItsAccess() =>
        EndpointCoverage.AssertEveryEndpointDeclaresAccess(typeof(DefaultUserTaskAccessPolicy).Assembly);
}
