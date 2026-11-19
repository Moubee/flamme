using Flamme.Workflows.ComponentTests.Fixtures;
using Flamme.Workflows.ComponentTests.Scenarios.Activities.Timers.Cron.Workflows;

namespace Flamme.Workflows.ComponentTests.Scenarios.Activities.Timers.Cron;

public class CronTests(App app) : TimerActivityTestBase(app)
{
    [Test]
    [DisplayName("Cron activity blocks workflow execution and resumes at next cron occurrence")]
    public async Task CronActivity_BlocksAndResumes()
    {
        await AssertActivityBlocksAndResumes(CronWorkflow.DefinitionId);
    }
}