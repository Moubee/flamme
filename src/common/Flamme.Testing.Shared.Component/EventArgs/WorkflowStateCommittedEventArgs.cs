using Flamme.Workflows;
using Flamme.Workflows.Management.Entities;
using Flamme.Workflows.State;

namespace Flamme.Testing.Shared;

public class WorkflowStateCommittedEventArgs(WorkflowExecutionContext workflowExecutionContext, WorkflowState workflowState, WorkflowInstance workflowInstance) : EventArgs 
{
    public WorkflowExecutionContext WorkflowExecutionContext { get; } = workflowExecutionContext;
    public WorkflowState WorkflowState { get; } = workflowState;
    public WorkflowInstance WorkflowInstance { get; } = workflowInstance;
}