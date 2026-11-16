using System.Reflection;
using Elsa.Workflows.Runtime.Entities;
using FastEndpoints;
namespace Elsa.Workflows.Api.UnitTests.ActivityExecutions;

public class ActivityExecutionsEndpointTests
{
    [Test]
    public async Task GetEndpoint_ExposesPathAndQueryRoutes()
    {
        var endpointType = Type.GetType("Elsa.Workflows.Api.Endpoints.ActivityExecutions.Get.Endpoint, Elsa.Workflows.Api", throwOnError: true)!;
        var endpoint = Activator.CreateInstance(endpointType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, [null], null)!;
        var definition = new EndpointDefinition(endpointType, typeof(EmptyRequest), typeof(ActivityExecutionRecord));

        endpointType
            .GetProperty("Definition", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .SetValue(endpoint, definition);

        endpointType.GetMethod("Configure")!.Invoke(endpoint, null);

        await Assert.That(definition.Routes).Contains("/activity-executions/{id}");
        await Assert.That(definition.Routes).Contains("/activity-executions/{*id}");
        await Assert.That(definition.Routes).Contains("/activity-executions/by-id");
    }
}
