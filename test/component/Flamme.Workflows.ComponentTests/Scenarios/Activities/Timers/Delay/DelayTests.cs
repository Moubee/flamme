using Flamme.Workflows.ComponentTests.Fixtures;
using Flamme.Workflows.ComponentTests.Scenarios.Activities.Timers.Delay.Workflows;

namespace Flamme.Workflows.ComponentTests.Scenarios.Activities.Timers.Delay;

public class DelayTests(App app) : TimerActivityTestBase(app)
{
    [Test]
    [DisplayName("Delay activity blocks workflow execution and resumes after specified duration")]
    public async Task DelayActivity_BlocksAndResumes()
    {
        await AssertActivityBlocksAndResumes(DelayWorkflow.DefinitionId);
    }
}