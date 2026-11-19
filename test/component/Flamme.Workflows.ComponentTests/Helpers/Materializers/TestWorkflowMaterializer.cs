using Flamme.Workflows.Activities;
using Flamme.Workflows.ComponentTests.WorkflowProviders;
using Flamme.Workflows.Management;
using Flamme.Workflows.Management.Entities;
using Flamme.Workflows.Runtime;

namespace Flamme.Workflows.ComponentTests.Materializers;

/// <summary>
/// A workflow materializer that deserializes workflows created from <see cref="TestWorkflowProvider"/>.
/// </summary>
public class TestWorkflowMaterializer(IEnumerable<IWorkflowsProvider> workflowProviders) : IWorkflowMaterializer
{
    /// <summary>
    /// The name of the materializer.
    /// </summary>
    public const string MaterializerName = "Test";

    /// <inheritdoc />
    public string Name => MaterializerName;

    /// <inheritdoc />
    public ValueTask<Workflow> MaterializeAsync(WorkflowDefinition definition, CancellationToken cancellationToken = default)
    {
        var testProvider = (TestWorkflowProvider)workflowProviders.Single(x => x is TestWorkflowProvider);
        var materializedWorkflow = testProvider.MaterializedWorkflows.First(x => x.Workflow.Identity.Id == definition.Id);

        return ValueTask.FromResult(materializedWorkflow.Workflow);
    }
}