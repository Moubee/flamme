using Flamme.Workflows.Management.Entities;

namespace Flamme.Workflows.Runtime.Matches;

public record ResumableWorkflowMatch(string WorkflowInstanceId, string? CorrelationId, string? BookmarkId, object? Payload)
    : WorkflowMatch(CorrelationId, Payload);