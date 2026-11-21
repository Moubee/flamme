namespace Flamme.Workflows.IntegrationTests.Scenarios.ProducerActivities;

public class ClosedActivityDiscoveryTests
{
    [Test]
    public async Task InfersNestedActivitiesFromConstructionAndTypeReferences()
    {
        var constructed = new Container<int>.Step();
        var constructedBinding = ActivityContextRegistry.GetRequiredBinding(constructed.GetType());
        var referencedBinding = ActivityContextRegistry.GetRequiredBinding(typeof(Container<string>.Step));

        await Assert.That(constructedBinding.IsInferred).IsTrue();
        await Assert.That(referencedBinding.IsInferred).IsTrue();
    }

    [Test]
    public async Task TupleNamesAndDynamicPayloadsShareOneBinding()
    {
        var named = typeof(TupleStep<((int X, int Y) Point, dynamic Value)[]>);
        var unnamed = typeof(TupleStep<((int, int), object)[]>);
        var context = ActivityContextRegistry.GetRequiredContext(typeof(ClosedActivityDiscoveryTests).Assembly);
        var binding = context.Bindings.Single(row => row.ActivityType == named);

        await Assert.That(ActivityContextRegistry.GetRequiredBinding(unnamed)).IsSameReferenceAs(binding);
    }

    internal class Container<T>
    {
        internal sealed class Step : CodeActivity;
    }

    internal sealed class TupleStep<T> : CodeActivity;
}
