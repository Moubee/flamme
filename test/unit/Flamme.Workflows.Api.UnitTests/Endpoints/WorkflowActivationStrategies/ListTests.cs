using Flamme.Extensions;
using Flamme.Workflows.ActivationValidators;
using Flamme.Workflows.Api.Endpoints.WorkflowActivationStrategies.List;
using Flamme.Workflows.Options;
using Flamme.Workflows.Services;
using Flamme.Common.Serialization;

namespace Flamme.Workflows.Api.UnitTests.Endpoints.WorkflowActivationStrategies;

public class ListTests
{
    [Test]
    public async Task ExecuteAsync_ReturnsWorkflowJsonTypeIdentifier_ForActivationStrategyTypeName()
    {
        var options = new SerializationTypeOptions();
        options.RegisterTypeAlias(typeof(AllowAlwaysStrategy), nameof(AllowAlwaysStrategy));
        options.RegisterLegacySimpleAssemblyQualifiedName(typeof(AllowAlwaysStrategy));
        var registry = new SerializationTypeRegistry(Microsoft.Extensions.Options.Options.Create(options));
        var endpoint = new List([new AllowAlwaysStrategy()], registry);

        var response = await endpoint.ExecuteAsync(CancellationToken.None);

        var descriptor = await Assert.That(response.Items).HasSingleItem();
        await Assert.That(descriptor.TypeName).IsEqualTo(nameof(AllowAlwaysStrategy));
        await Assert.That(registry.TryGetType(typeof(AllowAlwaysStrategy).GetSimpleAssemblyQualifiedName(), out var legacyType)).IsTrue();
        await Assert.That(legacyType).IsEqualTo(typeof(AllowAlwaysStrategy));
    }

    [Test]
    public async Task ExecuteAsync_WhenOnlyLegacyNameIsRegistered_AdvertisesResolvableIdentifier()
    {
        var options = new SerializationTypeOptions();
        options.RegisterLegacySimpleAssemblyQualifiedName(typeof(AllowAlwaysStrategy));
        var registry = new SerializationTypeRegistry(Microsoft.Extensions.Options.Options.Create(options));
        var endpoint = new List([new AllowAlwaysStrategy()], registry);

        var response = await endpoint.ExecuteAsync(CancellationToken.None);

        var descriptor = await Assert.That(response.Items).HasSingleItem();
        var expectedTypeName = typeof(AllowAlwaysStrategy).GetSimpleAssemblyQualifiedName();
        await Assert.That(descriptor.TypeName).IsEqualTo(expectedTypeName);
        await Assert.That(registry.TryGetType(descriptor.TypeName, out var resolvedType)).IsTrue();
        await Assert.That(resolvedType).IsEqualTo(typeof(AllowAlwaysStrategy));
    }
}
