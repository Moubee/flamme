using Flamme.Common.Models;
using Flamme.Workflows.ComponentTests.Abstractions;
using Flamme.Workflows.ComponentTests.Fixtures;
using Flamme.Workflows.ComponentTests.Scenarios.Activities.Composition.ExecuteWorkflows.Workflows;
using Flamme.Workflows.Models;
using Flamme.Workflows.Runtime;
using Flamme.Workflows.Runtime.Messages;
using Microsoft.Extensions.DependencyInjection;

namespace Flamme.Workflows.ComponentTests.Scenarios.Activities.Composition.ExecuteWorkflows;

public class ExecuteWorkflowsTests : AppComponentTest
{
    private IWorkflowRuntime _workflowRuntime = null!;

    public ExecuteWorkflowsTests(App app) : base(app)
    {
    }

    protected override ValueTask OnInitializeAsync()
    {
        _workflowRuntime = Scope.ServiceProvider.GetRequiredService<IWorkflowRuntime>();
        return ValueTask.CompletedTask;
    }

    [Test]
    public async Task ExecuteWorkflow_ShouldExecuteWorkflow()
    {
        var workflowClient = await _workflowRuntime.CreateClientAsync();
        await workflowClient.CreateInstanceAsync(new()
        {
            WorkflowDefinitionHandle = WorkflowDefinitionHandle.ByDefinitionId(MainWorkflow.DefinitionId, VersionOptions.Published)
        });
        await workflowClient.RunInstanceAsync(RunWorkflowInstanceRequest.Empty);
    }
}
