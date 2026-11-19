using Flamme.Testing.Shared.Authorization;
using Flamme.Workflows.Api.Endpoints.WorkflowDefinitions.List;

namespace Flamme.Workflows.Api.UnitTests.Authorization;

public class EndpointCoverageTests
{
    [Test]
    public void EveryWorkflowsApiEndpointDeclaresItsAccess() =>
        EndpointCoverage.AssertEveryEndpointDeclaresAccess(typeof(List).Assembly);
}
