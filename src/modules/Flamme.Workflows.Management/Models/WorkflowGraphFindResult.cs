using Flamme.Workflows.Management.Entities;
using Flamme.Workflows.Models;

namespace Flamme.Workflows.Management.Models;

public record WorkflowGraphFindResult(WorkflowDefinition? WorkflowDefinition, WorkflowGraph? WorkflowGraph)
{
    public bool WorkflowDefinitionExists => WorkflowDefinition != null;
    public bool WorkflowGraphExists => WorkflowGraph != null;
}