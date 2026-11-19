using Cronos;
using Flamme.Scheduling.Bookmarks;
using Flamme.Workflows.Activities;
using Flamme.Workflows.Management.Models;
using Flamme.Workflows.Runtime.Contracts;
using Flamme.Workflows.Runtime.Entities;

namespace Flamme.Scheduling.TriggerPayloadValidators;

public class CronTriggerPayloadValidator(ICronParser cronParser) : ITriggerPayloadValidator<CronTriggerPayload>
{
    public Task ValidateAsync(
        CronTriggerPayload payload,
        Workflow workflow,
        StoredTrigger trigger,
        ICollection<WorkflowValidationError> validationErrors,
        CancellationToken cancellationToken)
    {
        try
        {
            cronParser.GetNextOccurrence(payload.CronExpression);
        }
        catch (CronFormatException ex)
        {
            validationErrors.Add(new("Error when parsing cron expression: " + ex.Message,
                trigger.ActivityId));
        }
        return Task.CompletedTask;
    }
}