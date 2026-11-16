using System.Text.Json;
using Elsa.Common.Serialization;
using Elsa.Extensions;
using Elsa.Workflows.Models;
using Elsa.Workflows.Options;
using Elsa.Workflows.Runtime.ActivationValidators;
using Elsa.Workflows.Serialization.Converters;
using Elsa.Workflows.Services;

namespace Elsa.Workflows.Runtime.UnitTests.Serialization;

public class ActivationStrategyTypeAliasTests
{
    public static IEnumerable<(Type, string)> BuiltInActivationStrategies() =>
    [
        (typeof(SingletonStrategy), nameof(SingletonStrategy)),
        (typeof(CorrelatedSingletonStrategy), nameof(CorrelatedSingletonStrategy)),
        (typeof(CorrelationStrategy), nameof(CorrelationStrategy))
    ];

    [Test]
    [MethodDataSource(nameof(BuiltInActivationStrategies))]
    public async Task When_RegisterRuntimeAliases_Then_ResolvesPreferredAndLegacyNames(Type strategyType, string alias)
    {
        var registry = CreateRegistry();

        await Assert.That(registry.TryGetAlias(strategyType, out var registeredAlias)).IsTrue();
        await Assert.That(registeredAlias).IsEqualTo(alias);
        await Assert.That(registry.TryGetType(alias, out var byAlias)).IsTrue();
        await Assert.That(byAlias).IsEqualTo(strategyType);
        await Assert.That(registry.TryGetType(strategyType.GetSimpleAssemblyQualifiedName(), out var byLegacyName)).IsTrue();
        await Assert.That(byLegacyName).IsEqualTo(strategyType);
    }

    [Test]
    [MethodDataSource(nameof(BuiltInActivationStrategies))]
    public async Task When_DeserializeWorkflowOptions_Then_ResolvesActivationStrategyType(Type strategyType, string alias)
    {
        var options = CreateJsonOptions();
        var typeInfo = new TestJsonSerializerContext(options).WorkflowOptions;

        var byAlias = JsonSerializer.Deserialize($$"""{"activationStrategyType":{{JsonSerializer.Serialize(alias, TestJsonSerializerContext.Default.String)}}}""", typeInfo);
        await Assert.That(byAlias!.ActivationStrategyType).IsEqualTo(strategyType);

        var byLegacyName = JsonSerializer.Deserialize($$"""{"activationStrategyType":{{JsonSerializer.Serialize(strategyType.GetSimpleAssemblyQualifiedName(), TestJsonSerializerContext.Default.String)}}}""", typeInfo);
        await Assert.That(byLegacyName!.ActivationStrategyType).IsEqualTo(strategyType);

        var serialized = JsonSerializer.Serialize(new WorkflowOptions { ActivationStrategyType = strategyType }, typeInfo);
        await Assert.That(serialized).Contains(alias);
        await Assert.That(serialized).DoesNotContain("UnregisteredClrType:");

        var roundTrip = JsonSerializer.Deserialize(serialized, typeInfo);
        await Assert.That(roundTrip!.ActivationStrategyType).IsEqualTo(strategyType);
    }

    private static ISerializationTypeRegistry CreateRegistry()
    {
        var options = new SerializationTypeOptions();
        WorkflowRuntimeTypeAliasRegistrar.Register(options, []);
        return new SerializationTypeRegistry(Microsoft.Extensions.Options.Options.Create(options));
    }

    private static JsonSerializerOptions CreateJsonOptions() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new TypeJsonConverter(CreateRegistry()) }
    };
}
