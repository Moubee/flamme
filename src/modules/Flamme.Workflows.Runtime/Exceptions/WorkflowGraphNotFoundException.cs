using Flamme.Workflows.Models;

namespace Flamme.Workflows.Runtime.Exceptions;

[Obsolete("Use WorkflowDefinitionNotFoundException instead.")]
public class WorkflowGraphNotFoundException(string message, WorkflowDefinitionHandle workflowDefinitionHandle) : Exception(message)
{
    public WorkflowDefinitionHandle WorkflowDefinitionHandle { get; } = workflowDefinitionHandle;
}