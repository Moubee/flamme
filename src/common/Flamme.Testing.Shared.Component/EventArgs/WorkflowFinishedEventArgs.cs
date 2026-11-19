using Flamme.Workflows.Activities;
using Flamme.Workflows.State;

namespace Flamme.Testing.Shared;

public class WorkflowFinishedEventArgs(Workflow workflow, WorkflowState workflowState) : EventArgs
{
    public Workflow Workflow { get; } = workflow;
    public WorkflowState WorkflowState { get; } = workflowState;
}