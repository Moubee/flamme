using Flamme.Workflows.Memory;

namespace Flamme.Workflows;

/// <summary>
/// Provides context for storage drivers.
/// </summary>
public record StorageDriverContext(IExecutionContext ExecutionContext, Variable Variable, CancellationToken CancellationToken);