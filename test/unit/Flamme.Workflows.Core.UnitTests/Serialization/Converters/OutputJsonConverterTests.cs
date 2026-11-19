using System.Text.Json;
using Flamme.Common.Serialization;
using Flamme.Extensions;
using Flamme.Workflows.Memory;
using Flamme.Workflows.Models;
using Flamme.Workflows.Serialization.Converters;
using System.Threading.Tasks;

namespace Flamme.Workflows.Core.UnitTests.Serialization.Converters;

public sealed class OutputJsonConverterTests
{
    [Test]
    public async Task When_SerializeAndDeserializeConfiguredOutput_Then_ConverterConfigurationRoundTrips()
    {
        // Arrange
        var options = CreateOptions();
        var output = new Output<string>(new Variable("Result"))
        {
            Converter = new OutputConverterConfiguration("sample.to-text", CreateSettings())
        };

        // Act
        var json = JsonSerializer.Serialize(output, TestJsonTypeInfo.FromConverter<Output<string>>(options));
        var result = JsonSerializer.Deserialize(json, TestJsonTypeInfo.FromConverter<Output<string>>(options));

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.MemoryBlockReference().Id).IsEqualTo("resultVariable");
        var configuration = (await Assert.That(result.Converter).IsTypeOf<OutputConverterConfiguration>())!;
        await Assert.That(configuration.Id).IsEqualTo("sample.to-text");
        await Assert.That(configuration.Settings.HasValue).IsTrue();
        await Assert.That(configuration.Settings!.Value.GetProperty("format").GetString()).IsEqualTo("compact");

        using var document = JsonDocument.Parse(json);
        var converter = document.RootElement.GetProperty("converter");
        await Assert.That(converter.EnumerateObject().Select(x => x.Name))
            .IsEquivalentTo(new[] { "id", "settings" }, StringComparer.Ordinal, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task When_SerializeAndDeserializeUnconfiguredOutput_Then_ConverterPropertyIsOmitted()
    {
        // Arrange
        var options = CreateOptions();
        var output = new Output<string>(new Variable("Result"));

        // Act
        var json = JsonSerializer.Serialize(output, TestJsonTypeInfo.FromConverter<Output<string>>(options));
        var result = JsonSerializer.Deserialize(json, TestJsonTypeInfo.FromConverter<Output<string>>(options));

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result.Converter).IsNull();

        using var document = JsonDocument.Parse(json);
        await Assert.That(document.RootElement.EnumerateObject().Select(x => x.Name))
            .IsEquivalentTo(new[] { "typeName", "memoryReference" }, StringComparer.Ordinal, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(document.RootElement.GetProperty("typeName").GetString()).IsEqualTo("String");
        await Assert.That(document.RootElement.GetProperty("memoryReference").GetProperty("id").GetString()).IsEqualTo("resultVariable");
    }

    private static JsonSerializerOptions CreateOptions() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters =
        {
            new OutputJsonConverter<string>(SerializationTypeRegistry.CreateDefault())
        }
    };

    private static JsonElement CreateSettings()
    {
        using var document = JsonDocument.Parse("""{"format":"compact"}""");
        return document.RootElement.Clone();
    }
}
