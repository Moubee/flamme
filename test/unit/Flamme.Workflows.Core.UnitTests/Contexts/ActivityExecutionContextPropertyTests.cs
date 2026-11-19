using Flamme.Workflows.Core.UnitTests.Extensions.ActivityExecutionContextExtensions;

namespace Flamme.Workflows.Core.UnitTests.Contexts;

public class ActivityExecutionContextPropertyTests
{
    [Test]
    public async Task TransitionTo_OnlyTaintsWhenStatusChanges()
    {
        var context = await TestHelpers.CreateContextAsync();
        context.ClearTaint();

        context.TransitionTo(context.Status);

        await Assert.That(context.IsDirty).IsFalse();

        context.TransitionTo(ActivityStatus.Running);

        await Assert.That(context.Status).IsEqualTo(ActivityStatus.Running);
        await Assert.That(context.IsDirty).IsTrue();
    }

    [Test]
    public async Task ExceptionAssignment_TaintsEvenWhenClearingTheException()
    {
        var context = await TestHelpers.CreateContextAsync();
        var exception = new InvalidOperationException("Activity failed.");
        context.ClearTaint();

        context.Exception = exception;

        await Assert.That(context.Exception).IsEqualTo(exception);
        await Assert.That(context.IsDirty).IsTrue();

        context.ClearTaint();
        context.Exception = null;

        await Assert.That(context.Exception).IsNull();
        await Assert.That(context.IsDirty).IsTrue();
    }

    [Test]
    public async Task CreatingAChildContext_RegistersItWithItsParent()
    {
        var contexts = await TestHelpers.CreateContextChainAsync(2);
        var parent = contexts[0];
        var child = contexts[1];

        await Assert.That(parent.ParentActivityExecutionContext).IsNull();
        await Assert.That(child.ParentActivityExecutionContext).IsEqualTo(parent);
        await Assert.That(parent.Children.Contains(child)).IsTrue();
    }
}
