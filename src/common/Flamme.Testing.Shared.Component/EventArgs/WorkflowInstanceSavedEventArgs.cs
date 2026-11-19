using Flamme.Workflows.Management.Entities;

namespace Flamme.Testing.Shared;

public class WorkflowInstanceSavedEventArgs(WorkflowInstance workflowInstance) : EventArgs 
{
    public WorkflowInstance WorkflowInstance { get; } = workflowInstance;
}