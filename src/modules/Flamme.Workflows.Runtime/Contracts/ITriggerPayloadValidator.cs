using Flamme.Workflows.Activities;
using Flamme.Workflows.Management.Models;
using Flamme.Workflows.Runtime.Entities;

namespace Flamme.Workflows.Runtime.Contracts;

/// <summary>
/// Validator that validate a given trigger payload.
/// </summary>
public interface ITriggerPayloadValidator<TPayload>
{
    /// <summary>
    /// Validate a trigger payload. If trigger is not valid, error will be add in this list.
    /// </summary>
    Task ValidateAsync(
        TPayload payload,
        Workflow workflow,
        StoredTrigger trigger,
        ICollection<WorkflowValidationError> validationErrors,
        CancellationToken cancellationToken);
}