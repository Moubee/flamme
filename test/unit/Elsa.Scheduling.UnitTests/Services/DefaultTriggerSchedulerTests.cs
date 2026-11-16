using Elsa.Common;
using Elsa.Scheduling.Activities;
using Elsa.Scheduling.Bookmarks;
using Elsa.Scheduling.Services;
using Elsa.Workflows.Helpers;
using Elsa.Workflows.Runtime.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Elsa.Scheduling.UnitTests.Services;

public class DefaultTriggerSchedulerTests
{
    [Test]
    public async Task ScheduleAsync_SchedulesPastDueStartAtTriggerForCatchUp()
    {
        var workflowScheduler = IWorkflowScheduler.Mock().Object;
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var logger = ILogger<DefaultTriggerScheduler>.Mock().Object;
        var scheduler = new DefaultTriggerScheduler(workflowScheduler, timeProvider, logger);
        var now = new DateTimeOffset(2025, 11, 06, 22, 50, 00, TimeSpan.Zero);
        var executeAt = now.AddMinutes(-5);
        ScheduleNewWorkflowInstanceRequest? scheduledRequest = null;
        var trigger = new StoredTrigger
        {
            Id = "trigger-1",
            Name = ActivityTypeNameHelper.GenerateTypeName<StartAt>(),
            WorkflowDefinitionVersionId = "workflow-version",
            ActivityId = "activity-1",
            Payload = new StartAtPayload(executeAt)
        };
        timeProvider.SetUtcNow(now);
        Mock.Get(workflowScheduler).ScheduleAtAsync(trigger.Id, Arg.Any<ScheduleNewWorkflowInstanceRequest>(), executeAt, Arg.Any<CancellationToken>()).Callback((string _, ScheduleNewWorkflowInstanceRequest request, DateTimeOffset _, CancellationToken _) => scheduledRequest = request);

        await scheduler.ScheduleAsync([trigger], CancellationToken.None);

        Mock.Get(workflowScheduler).ScheduleAtAsync(trigger.Id, Arg.Any<ScheduleNewWorkflowInstanceRequest>(), executeAt, Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
        await Assert.That(scheduledRequest).IsNotNull();
        await Assert.That(scheduledRequest.TriggerActivityId).IsEqualTo(trigger.ActivityId);
        await Assert.That(scheduledRequest.WorkflowDefinitionHandle.DefinitionVersionId).IsEqualTo(trigger.WorkflowDefinitionVersionId);
    }
}
