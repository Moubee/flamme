using System.Net;
using Flamme.Api.Client.Resources.WorkflowDefinitions.Contracts;
using Flamme.Testing.Shared.Extensions;
using Flamme.Workflows.Api.Endpoints.WorkflowDefinitions.Execute;
using Flamme.Workflows.ComponentTests.Abstractions;
using Flamme.Workflows.ComponentTests.Fixtures;

namespace Flamme.Workflows.ComponentTests.Scenarios.BasicWorkflows;

public class HelloWorldTests(App app) : AppComponentTest(app)
{
    [Test]
    public async Task HelloWorldWorkflow_ShouldReturnOk()
    {
        var client = WorkflowServer.CreateApiClient<IExecuteWorkflowApi>();
        using var response = await client.ExecuteAsync("1590068018aa4f0a");
        var model = await response.ReadAsJsonAsync<Response>(WorkflowServer.Services);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(model.WorkflowState.SubStatus).IsEqualTo(WorkflowSubStatus.Finished);
    }
}