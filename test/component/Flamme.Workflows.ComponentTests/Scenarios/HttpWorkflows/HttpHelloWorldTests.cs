using Flamme.Workflows.ComponentTests.Abstractions;
using Flamme.Workflows.ComponentTests.Fixtures;

namespace Flamme.Workflows.ComponentTests.Scenarios.HttpWorkflows;

public class HttpHelloWorldTests(App app) : AppComponentTest(app)
{
    [Test]
    public async Task HelloWorldWorkflow_ShouldRespondWithHelloWorld()
    {
        var client = WorkflowServer.CreateHttpWorkflowClient();
        var response = await client.GetStringAsync("hello-world");
        await Assert.That(response).IsEqualTo("Hello World!");
    }
}