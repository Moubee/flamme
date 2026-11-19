using Elsa.Extensions;
using Elsa.Testing.Shared;
using Elsa.Workflows.Activities;
using Elsa.Workflows.Memory;
using Elsa.Workflows.Options;

namespace Elsa.Workflows.Core.UnitTests.Contexts;

public class ScheduledVariableIsolationTests
{
    [Test]
    public async Task CreateActivityExecutionContext_DoesNotOverwriteParentMemoryBlocks_WithTheSameVariableNames()
    {
        var inner = new Sequence();
        var outer = new Sequence
        {
            Activities = { inner }
        };
        var fixture = new ActivityTestFixture(outer);
        var outerContext = await fixture.BuildAsync();
        var workflowExecutionContext = outerContext.WorkflowExecutionContext;

        var parentCurrentValue = new Variable("CurrentValue", "outer-value");
        var parentCurrentIndex = new Variable("CurrentIndex", 0);
        outerContext.DynamicVariables.Add(parentCurrentValue);
        outerContext.DynamicVariables.Add(parentCurrentIndex);
        outerContext.ExpressionExecutionContext.Memory.Declare(parentCurrentValue);
        outerContext.ExpressionExecutionContext.Memory.Declare(parentCurrentIndex);
        parentCurrentValue.Set(outerContext.ExpressionExecutionContext, "outer-value");
        parentCurrentIndex.Set(outerContext.ExpressionExecutionContext, 0);

        var innerContext = await workflowExecutionContext.CreateActivityExecutionContextAsync(inner, new ActivityInvocationOptions
        {
            Owner = outerContext,
            Variables =
            [
                new Variable("CurrentValue", "inner-value"),
                new Variable("CurrentIndex", 7)
            ]
        });

        await Assert.That(outerContext.ExpressionExecutionContext.GetVariable<string>("CurrentValue")).IsEqualTo("outer-value");
        await Assert.That(outerContext.ExpressionExecutionContext.GetVariable<int>("CurrentIndex")).IsEqualTo(0);
        await Assert.That(innerContext.ExpressionExecutionContext.GetVariable<string>("CurrentValue")).IsEqualTo("inner-value");
        await Assert.That(innerContext.ExpressionExecutionContext.GetVariable<int>("CurrentIndex")).IsEqualTo(7);
    }
}
