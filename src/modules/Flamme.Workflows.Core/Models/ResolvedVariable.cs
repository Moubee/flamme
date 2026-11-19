using Flamme.Workflows.Memory;

namespace Flamme.Workflows;

/// <summary>
/// Represents a variable and its value.
/// </summary>
public record ResolvedVariable(Variable Variable, object? Value);