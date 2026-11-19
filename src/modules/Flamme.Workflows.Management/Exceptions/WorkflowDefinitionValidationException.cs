namespace Flamme.Workflows.Management.Exceptions;

/// <summary>Indicates that a workflow definition change failed validation before it could be saved.</summary>
public sealed class WorkflowDefinitionValidationException(string message) : InvalidOperationException(message);
