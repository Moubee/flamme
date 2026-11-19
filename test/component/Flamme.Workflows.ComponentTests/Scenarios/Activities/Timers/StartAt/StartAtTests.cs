using Flamme.Workflows.ComponentTests.Fixtures;
using Flamme.Workflows.ComponentTests.Scenarios.Activities.Timers.StartAt.Workflows;

namespace Flamme.Workflows.ComponentTests.Scenarios.Activities.Timers.StartAt;

public class StartAtTests(App app) : TimerActivityTestBase(app)
{
    [Test]
    [DisplayName("StartAt activity blocks workflow execution and resumes at specified time")]
    public async Task StartAtActivity_BlocksAndResumes()
    {
        await AssertActivityBlocksAndResumes(StartAtWorkflow.DefinitionId);
    }
}