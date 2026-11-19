using Flamme.Workflows.Models;
using Flamme.Workflows.Runtime.Entities;

namespace Flamme.Workflows.Runtime;

/// <summary>
/// Represents a workflow bound to one or more triggers.
/// </summary>
public record TriggerBoundWorkflow(WorkflowGraph WorkflowGraph, ICollection<StoredTrigger> Triggers);