using Flamme.Mediator.Contracts;
using Flamme.Workflows.Activities;
using Flamme.Workflows.Management.Models;
using Flamme.Workflows.Management.Notifications;

namespace Flamme.Workflows.Management.Services;

/// <inheritdoc />
public class WorkflowValidator(INotificationSender notificationSender) : IWorkflowValidator
{
    /// <inheritdoc />
    public async Task<IEnumerable<WorkflowValidationError>> ValidateAsync(Workflow workflow, CancellationToken cancellationToken = default)
    {
        var validationErrors = new List<WorkflowValidationError>();
        var notification = new WorkflowDefinitionValidating(workflow, validationErrors);
        await notificationSender.SendAsync(notification, cancellationToken);
        return validationErrors;
    }
}