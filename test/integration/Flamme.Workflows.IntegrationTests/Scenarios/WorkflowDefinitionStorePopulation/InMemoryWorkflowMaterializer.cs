using Flamme.Workflows.Activities;
using Flamme.Workflows.Management;
using Flamme.Workflows.Management.Entities;

namespace Flamme.Workflows.IntegrationTests.Scenarios.WorkflowDefinitionStorePopulation;

public class InMemoryWorkflowMaterializer(Workflow workflow) : IWorkflowMaterializer
{
    public string Name => "InMemory";
    
    public ValueTask<Workflow> MaterializeAsync(WorkflowDefinition definition, CancellationToken cancellationToken = default)
    {
        var materializedWorkflow = new Workflow
        {
            Identity = new(definition.DefinitionId, definition.Version, definition.Id),
            Root = workflow.Root
        };
        return new(materializedWorkflow);
    }
}