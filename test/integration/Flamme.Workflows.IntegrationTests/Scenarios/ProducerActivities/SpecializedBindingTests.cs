using System.Collections;
using Flamme.Expressions.Models;
using Flamme.Extensions;
using Flamme.Testing.Shared;
using Flamme.Testing.Shared.Activities;
using Flamme.Workflows.Attributes;
using Flamme.Workflows.Exceptions;
using Flamme.Workflows.Models;

namespace Flamme.Workflows.IntegrationTests.Scenarios.ProducerActivities;

public class SpecializedBindingTests
{
    [Test]
    public async Task ClosedOwnerDeterminesPrivateCallbackCompatibility()
    {
        var resumable = new ResumableProducerActivity<ActivityExecutionContext>();
        await resumable.GetResumeActivityDelegate("Resume")(null!);
        await Assert.That(resumable.Resumed).IsTrue();

        var other = new ResumableProducerActivity<string>();
        var binding = ActivityContextRegistry.GetRequiredBinding(other.GetType());
        await Assert.That(binding.BindBookmarkCallback(other, "Resume") is null).IsTrue();
    }

    [Test]
    public async Task StructCollectionsBoxEachChildAndHybridActivitiesRemainSinglePorts()
    {
        var activity = new ChildContainer();
        var binding = ActivityContextRegistry.GetRequiredBinding(activity.GetType());
        var ports = binding.GetActivityPorts(activity).ToDictionary(port => port.PortName);

        await Assert.That(ports[nameof(ChildContainer.Hybrid)].Activity).IsSameReferenceAs(activity.Hybrid);
        await Assert.That(ports[nameof(ChildContainer.Values)].Activities!.Select(step => step.Id).ToArray())
            .IsEquivalentTo(["first", "second"]);
        await Assert.That(ports[nameof(ChildContainer.References)].Activities!.Single()).IsSameReferenceAs(activity.Hybrid);
    }

    [Test]
    public async Task InitializedAndOmittedInputSubclassesUseTheirDeclaredTypes()
    {
        var activity = new WrappedInputs();
        await using var fixture = new ActivityTestFixture(activity);
        await fixture.ExecuteAsync();

        await Assert.That(activity.Result).IsEqualTo(45);
        await Assert.That(activity.Omitted).IsNotNull();
    }

    [Test]
    public async Task MissingSubclassFactoryIsReportedOnlyWhenADefaultMustBeCreated()
    {
        await using var fixture = new ActivityTestFixture(new WrappedInputs { Initialized = null });
        var error = await Assert.ThrowsAsync<InputEvaluationException>(() => fixture.ExecuteAsync());
        await Assert.That(error.InnerException!.Message).Contains("wrapped input constructor for 'Initialized'");
    }

    public sealed class ChildContainer : CodeActivity
    {
        public HybridChild Hybrid { get; } = new();
        public List<ValueChild> Values { get; } = [new() { Id = "first" }, new() { Id = "second" }];
        public IReadOnlyList<HybridChild> References => [Hybrid];
    }

    public sealed class HybridChild : CodeActivity, IEnumerable<ValueChild>
    {
        public IEnumerator<ValueChild> GetEnumerator() => Enumerable.Empty<ValueChild>().GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public struct ValueChild : IActivity
    {
        public string Id { get; set; }
        public string NodeId { get; set; }
        public string? Name { get; set; }
        public string Type { get; set; }
        public int Version { get; set; }
        public IDictionary<string, object> CustomProperties { get; set; }
        public IDictionary<string, object> SyntheticProperties { get; set; }
        public IDictionary<string, object> Metadata { get; set; }
        public ValueTask<bool> CanExecuteAsync(ActivityExecutionContext context) => new(true);
        public ValueTask ExecuteAsync(ActivityExecutionContext context) => default;
    }

    public sealed class CachedInput<T>(T value) : Input<T>(value);

    public sealed class DefaultableInput<T>(Literal value) : Input<T>(value);

    public sealed class WrappedInputs : CodeActivity
    {
        [Input(DefaultValue = 9)]
        public CachedInput<int>? Initialized { get; set; } = new(42);

        [Input(DefaultValue = 3)]
        public DefaultableInput<int>? Omitted { get; set; }

        public int Result { get; private set; }
        protected override void Execute(ActivityExecutionContext context) => Result = context.Get(Initialized) + context.Get(Omitted);
    }
}
