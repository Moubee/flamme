using Flamme.Extensions;
using Flamme.Testing.Shared;
using Flamme.Workflows.Activities;
using Flamme.Workflows.Attributes;
using Flamme.Workflows.Management;
using Flamme.Workflows.Management.Extensions;
using Flamme.Workflows.Management.Options;
using Flamme.Workflows.Memory;
using Flamme.Workflows.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Flamme.Workflows.IntegrationTests.Scenarios.ProducerActivities;

public class ActivityBindingTests
{
    [Test]
    public async Task AssemblyAndContextRegistrationsSelectTheirDeclaredActivities()
    {
        var services = new ServiceCollection();
        services.AddActivitiesFrom(typeof(Increment).Assembly);
        services.AddActivities(ReceiptActivities.Default);
        services.Configure<ManagementOptions>(options => options.ActivityTypes.Remove(typeof(Removed)));
        using var provider = services.BuildServiceProvider();

        var selected = provider.GetRequiredService<IOptions<ManagementOptions>>().Value.ActivityTypes;
        await Assert.That(selected.Contains(typeof(Increment))).IsTrue();
        await Assert.That(selected.Contains(typeof(SetVariable<Receipt>))).IsTrue();
        await Assert.That(selected.Contains(typeof(InternalStep))).IsTrue();
        await Assert.That(selected.Contains(typeof(Removed))).IsFalse();

        var exported = ActivityContextRegistry.GetRequiredContext(typeof(Increment).Assembly).ExportedActivityTypes;
        await Assert.That(exported.Contains(typeof(Increment))).IsTrue();
        await Assert.That(exported.Contains(typeof(InternalStep))).IsFalse();
    }

    [Test]
    public async Task MissingBindingIdentifiesTheSelectedActivityAndModule()
    {
        var services = new ServiceCollection();
        services.AddActivities([typeof(Unbound)]);
        using var provider = services.BuildServiceProvider();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Task.FromResult(provider.GetRequiredService<IOptions<ManagementOptions>>().Value));
        await Assert.That(exception.Message).Contains(typeof(Unbound).FullName!);
        await Assert.That(exception.Message).Contains(typeof(Unbound).Assembly.GetName().Name!);
        await Assert.That(exception.Message).Contains("binding");
    }

    [Test]
    public async Task SequenceExecutesInheritedOutputAndConstructsOmittedWrappedInput()
    {
        var first = new Increment { Id = "first" };
        var next = new Variable<int>("next", 0);
        first.Number = new(3);
        first.Native = new(next);
        var second = new Increment { Id = "second", Number = new(next) };
        var third = new Increment { Id = "third" };
        var sequence = new Sequence { Variables = [next], Activities = [first, second, third] };

        await using var host = (ServiceProvider)new TestApplicationBuilder(TestContext.Current!.Output.StandardOutput)
            .ConfigureFlamme(flamme => flamme.AddActivity<Increment>()).Build();
        using var scope = host.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IActivityRegistryPopulator>().EnsureRegistryPopulatedAsync(default);
        var registry = scope.ServiceProvider.GetRequiredService<IActivityRegistry>();
        var descriptor = registry.Find(first.Type, first.Version)!;
        var output = descriptor.Outputs.Single();
        await Assert.That(output.Name).IsEqualTo("Published");
        await Assert.That(output.MemberBinding!.DeclaringType).IsEqualTo(typeof(OutputStep));
        await Assert.That(output.ValueGetter(first)).IsSameReferenceAs(first.Native);

        var runner = scope.ServiceProvider.GetRequiredService<IWorkflowRunner>();
        var run = await runner.RunAsync(sequence);
        var outputs = run.WorkflowExecutionContext.GetActivityOutputRegister();
        await Assert.That(run.WorkflowState.SubStatus).IsEqualTo(WorkflowSubStatus.Finished);
        await Assert.That(outputs.FindOutputByActivityId(first.Id, "Published")).IsEqualTo(4);
        await Assert.That(outputs.FindOutputByActivityId(second.Id, "Published")).IsEqualTo(5);
        await Assert.That(outputs.FindOutputByActivityId(third.Id, "Published")).IsEqualTo(8);
        await Assert.That(third.Number).IsNotNull();
        await Assert.That(second.Native.MemoryBlockReference().Id).IsEqualTo("second:output-native");
    }

    [Test]
    public async Task InheritedChildrenAndPrivateCompletionCallbackAreBoundAcrossAssemblies()
    {
        var child = new Increment();
        var sequence = new ProducerSequence { Activities = [child] };
        var binding = ActivityContextRegistry.GetRequiredBinding(typeof(ProducerSequence));
        var port = binding.GetActivityPorts(sequence).Single(p => p.PortName == nameof(Sequence.Activities));
        await Assert.That(port.GetActivities().Single()).IsSameReferenceAs(child);
        var callback = binding.BindCompletionCallback(sequence, "OnChildCompleted");
        await Assert.That(callback is not null).IsTrue();
    }

    public abstract class OutputStep : CodeActivity
    {
        [Output(Name = "Published")]
        public Output<int> Native { get; set; } = new();
    }

    public sealed class Increment : OutputStep
    {
        [Input(DefaultValue = 7)]
        public Input<int>? Number { get; set; }

        protected override void Execute(ActivityExecutionContext context) => Native.Set(context, context.Get(Number) + 1);
    }

    public sealed class Removed : CodeActivity;
    internal sealed class InternalStep : CodeActivity;
    internal sealed class ProducerSequence : Sequence;
    internal sealed record Receipt(int Number);
    private sealed class Unbound : CodeActivity;
}

[ActivityBinding(typeof(ActivityBindingTests.InternalStep))]
[ActivityBinding(typeof(SetVariable<ActivityBindingTests.Receipt>))]
internal partial class ReceiptActivities : ActivityContext;
