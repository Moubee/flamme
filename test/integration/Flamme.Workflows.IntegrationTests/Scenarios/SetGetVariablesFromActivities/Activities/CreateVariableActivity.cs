using Flamme.Extensions;

namespace Flamme.Workflows.IntegrationTests.Scenarios.SetGetVariablesFromActivities.Activities;

public class CreateVariableActivity : CodeActivity
{
    protected override void Execute(ActivityExecutionContext context)
    {
        context.SetVariable("Foo", "Bar");
    }
}