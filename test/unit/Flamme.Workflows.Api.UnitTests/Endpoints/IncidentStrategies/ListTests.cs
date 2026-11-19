using Flamme.Extensions;
using Flamme.Workflows.Api.Endpoints.IncidentStrategies.List;
using Flamme.Workflows.IncidentStrategies;
using Flamme.Workflows.Options;
using Flamme.Workflows.Services;
using Flamme.Common.Serialization;
namespace Flamme.Workflows.Api.UnitTests.Endpoints.IncidentStrategies;

public class ListTests
{
    [Test]
    public async Task ExecuteAsync_ReturnsWorkflowJsonTypeIdentifier_ForIncidentStrategyTypeName()
    {
        var options = new SerializationTypeOptions();
        options.RegisterTypeAlias(typeof(ContinueWithIncidentsStrategy), nameof(ContinueWithIncidentsStrategy));
        options.RegisterLegacySimpleAssemblyQualifiedName(typeof(ContinueWithIncidentsStrategy));
        var registry = new SerializationTypeRegistry(Microsoft.Extensions.Options.Options.Create(options));
        var endpoint = new List([new ContinueWithIncidentsStrategy()], registry);

        var response = await endpoint.ExecuteAsync(CancellationToken.None);

        var descriptor = await Assert.That(response.Items).HasSingleItem();
        await Assert.That(descriptor.TypeName).IsEqualTo(nameof(ContinueWithIncidentsStrategy));
        await Assert.That(registry.TryGetType(typeof(ContinueWithIncidentsStrategy).GetSimpleAssemblyQualifiedName(), out var legacyType)).IsTrue();
        await Assert.That(legacyType).IsEqualTo(typeof(ContinueWithIncidentsStrategy));
    }
}
