using System.Net;
using Flamme.Api.Client.Resources.WorkflowDefinitions.Contracts;
using Flamme.Testing.Shared.Extensions;
using Flamme.Workflows.Api.Endpoints.WorkflowDefinitions.Execute;
using Flamme.Workflows.ComponentTests.Abstractions;
using Flamme.Workflows.ComponentTests.Fixtures;

namespace Flamme.Workflows.ComponentTests.Scenarios.WorkflowCompletion;

public class WorkflowCompletionTests(App app) : AppComponentTest(app)
{
    [Test]
    [Arguments("2630068018ac1f0a")]
    [Arguments("5590069018aa4f0e")]
    public async Task Workflow_ShouldComplete(string workflowDefinitionId)
    {
        var client = WorkflowServer.CreateApiClient<IExecuteWorkflowApi>();
        using var response = await client.ExecuteAsync(workflowDefinitionId);
        var model = await response.ReadAsJsonAsync<Response>(WorkflowServer.Services);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(model.WorkflowState.SubStatus).IsEqualTo(WorkflowSubStatus.Finished);
    }
}