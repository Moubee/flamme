using Flamme.Workflows.Models;
using Flamme.Workflows.Runtime.Entities;

namespace Flamme.Http;

/// <summary>
/// Represents the result of a workflow lookup.
/// </summary>
public record HttpWorkflowLookupResult(WorkflowGraph? WorkflowGraph, ICollection<StoredTrigger> Triggers);