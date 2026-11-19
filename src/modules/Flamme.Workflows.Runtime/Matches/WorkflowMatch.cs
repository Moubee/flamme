using Flamme.Workflows.Management.Entities;

namespace Flamme.Workflows.Runtime.Matches;

public record WorkflowMatch(string? CorrelationId, object? Payload);