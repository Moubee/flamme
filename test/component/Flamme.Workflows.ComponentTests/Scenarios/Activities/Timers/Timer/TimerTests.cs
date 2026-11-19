using Flamme.Workflows.ComponentTests.Fixtures;
using Flamme.Workflows.ComponentTests.Scenarios.Activities.Timers.Timer.Workflows;

namespace Flamme.Workflows.ComponentTests.Scenarios.Activities.Timers.Timer;

public class TimerTests(App app) : TimerActivityTestBase(app)
{
    [Test]
    [DisplayName("Timer activity blocks workflow execution and resumes after specified interval")]
    public async Task TimerActivity_BlocksAndResumes()
    {
        await AssertActivityBlocksAndResumes(TimerWorkflow.DefinitionId);
    }
}