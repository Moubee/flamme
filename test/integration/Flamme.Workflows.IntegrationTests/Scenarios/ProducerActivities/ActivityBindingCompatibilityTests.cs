using Flamme.Extensions;
using Flamme.Testing.Shared;
using Flamme.Testing.Shared.Activities;
using Flamme.Workflows.Activities;
using Flamme.Workflows.Management;
using Microsoft.Extensions.DependencyInjection;

[assembly: Flamme.Workflows.ActivityBinding(typeof(TestContainer))]

namespace Flamme.Workflows.IntegrationTests.Scenarios.ProducerActivities;

public class ActivityBindingCompatibilityTests
{
    [Test]
    public async Task IndependentlyGeneratedExplicitBindingsCanRegisterTogether()
    {
        var producer = ActivityContextRegistry.GetRequiredContext(typeof(TestContainer).Assembly);
        var consumer = ActivityContextRegistry.GetRequiredContext(typeof(ActivityBindingCompatibilityTests).Assembly);
        var original = producer.Bindings.Single(binding => binding.ActivityType == typeof(TestContainer));
        var duplicate = consumer.Bindings.Single(binding => binding.ActivityType == typeof(TestContainer));

        await Assert.That(original.IsInferred || duplicate.IsInferred).IsFalse();
        await Assert.That(ReferenceEquals(original, duplicate)).IsFalse();
        var canonical = ActivityContextRegistry.GetRequiredBinding(typeof(TestContainer));
        await Assert.That(canonical == original || canonical == duplicate).IsTrue();
    }

    [Test]
    public async Task ConflictingMemberMetadataStillFailsRegistration()
    {
        var original = ActivityContextRegistry.GetRequiredBinding(typeof(SetVariable<List<int>[]>));
        var conflicting = new GeneratedActivityBinding
        {
            ActivityType = original.ActivityType,
            TypeName = original.TypeName,
            Name = original.Name,
            Namespace = original.Namespace,
            Version = original.Version,
            Inputs = [],
            Outputs = original.Outputs,
            CreateDescriptor = original.CreateDescriptor,
            GetActivityPorts = original.GetActivityPorts,
            GetOutcomeName = original.GetOutcomeName,
            PublicPropertyNamesFactory = () => original.PublicPropertyNames,
            BindCompletionCallback = original.BindCompletionCallback,
            BindBookmarkCallback = original.BindBookmarkCallback
        };

        var error = Assert.ThrowsExactly<InvalidOperationException>(() => ActivityContextRegistry.RegisterBindings(new ConflictingContext(conflicting)));
        await Assert.That(error.Message).Contains("conflicting generated binding");
        await Assert.That(ActivityContextRegistry.GetRequiredBinding(original.ActivityType)).IsSameReferenceAs(original);
    }

    [Test]
    public async Task SavedArrayOfGenericPayloadResolvesByItsOriginalTypeName()
    {
        await using var host = (ServiceProvider)new TestApplicationBuilder(TestContext.Current!.Output.StandardOutput)
            .ConfigureFlamme(flamme => flamme.AddActivity<SetVariable<List<int>[]>>()).Build();
        using var scope = host.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IActivityRegistryPopulator>().EnsureRegistryPopulatedAsync(default);
        var serializer = scope.ServiceProvider.GetRequiredService<IActivitySerializer>();
        var activity = serializer.Deserialize("""{"id":"step","type":"Data.SetVariable<List`1[]>","version":1}""");

        await Assert.That(activity.GetType()).IsEqualTo(typeof(SetVariable<List<int>[]>));
        await Assert.That(activity.Type).IsEqualTo("Data.SetVariable<List`1[]>");
    }

    private sealed class ConflictingContext(GeneratedActivityBinding row)
        : ActivityContext(typeof(ConflictingContext).Assembly, [row], []);
}
