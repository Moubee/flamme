using Elsa.Alterations.Core.Contracts;
using Elsa.Alterations.Core.Entities;
using Elsa.Alterations.Core.Enums;
using Elsa.Alterations.Core.Filters;
using Elsa.Alterations.Core.Models;
using Elsa.Alterations.Core.Results;
using Elsa.Alterations.Services;
using Elsa.Mediator.Contracts;
using Microsoft.Extensions.Time.Testing;

namespace Elsa.Alterations.IntegrationTests;

public class AlterationJobTimeTests
{
    [Test]
    public async Task RunAsync_RecordsTheStartAndCompletionInstantsSeparately()
    {
        var initialUtc = new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero).AddTicks(1234567);
        var time = new FakeTimeProvider(initialUtc);
        var job = new AlterationJob { Id = "job", PlanId = "plan", WorkflowInstanceId = "instance" };
        var plan = new AlterationPlan { Id = "plan" };
        var jobs = Mock.Of<IAlterationJobStore>().Object;
        var plans = Mock.Of<IAlterationPlanStore>().Object;
        Mock.Get(jobs).FindAsync(Arg.Any<AlterationJobFilter>(), Arg.Any<CancellationToken>()).Returns(job);
        Mock.Get(plans).FindAsync(Arg.Any<AlterationPlanFilter>(), Arg.Any<CancellationToken>()).Returns(plan);
        var runner = new DefaultAlterationJobRunner(plans, jobs, new AdvancingRunner(time), Mock.Of<INotificationSender>().Object, time);

        var result = await runner.RunAsync(job.Id);

        await Assert.That(result.StartedAt).IsEqualTo(initialUtc);
        await Assert.That(result.CompletedAt).IsEqualTo(initialUtc.AddMinutes(5));
        await Assert.That(result.Status).IsEqualTo(AlterationJobStatus.Completed);
        var log = await Assert.That(result.Log).IsNotNull();
        await Assert.That(log.Single().Timestamp).IsEqualTo(initialUtc.AddMinutes(5));
    }

    private sealed class AdvancingRunner(FakeTimeProvider time) : IAlterationRunner
    {
        public Task<RunAlterationsResult> RunAsync(string workflowInstanceId, IEnumerable<IAlteration> alterations, CancellationToken cancellationToken = default)
        {
            time.Advance(TimeSpan.FromMinutes(5));
            var log = new AlterationLog(time);
            log.Add("completed");
            return Task.FromResult(new RunAlterationsResult(workflowInstanceId, log));
        }

        public Task<ICollection<RunAlterationsResult>> RunAsync(IEnumerable<string> workflowInstanceIds, IEnumerable<IAlteration> alterations, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
