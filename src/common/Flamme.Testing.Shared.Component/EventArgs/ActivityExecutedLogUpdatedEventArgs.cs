using Flamme.Workflows;
using Flamme.Workflows.Runtime.Entities;

namespace Flamme.Testing.Shared;

public class ActivityExecutedLogUpdatedEventArgs(WorkflowExecutionContext workflowExecutionContext, ICollection<ActivityExecutionRecord> records) : EventArgs
{
    public WorkflowExecutionContext WorkflowExecutionContext { get; } = workflowExecutionContext;
    public ICollection<ActivityExecutionRecord> Records { get; } = records;
}