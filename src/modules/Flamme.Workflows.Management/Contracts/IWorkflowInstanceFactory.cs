using Flamme.Workflows.Activities;
using Flamme.Workflows.Management.Entities;
using Flamme.Workflows.Management.Options;
using Flamme.Workflows.State;

namespace Flamme.Workflows.Management.Contracts;

/// <summary>
/// Creates new <see cref="WorkflowInstance"/> objects.
/// </summary>
public interface IWorkflowInstanceFactory
{
    /// <summary>
    /// Creates a new <see cref="WorkflowState"/> object.
    /// </summary>
    WorkflowState CreateWorkflowState(Workflow workflow, WorkflowInstanceOptions? options = null);

    /// <summary>
    /// Creates a new <see cref="WorkflowInstance"/> object.
    /// </summary>
    WorkflowInstance CreateWorkflowInstance(Workflow workflow, WorkflowInstanceOptions? options = null);
}