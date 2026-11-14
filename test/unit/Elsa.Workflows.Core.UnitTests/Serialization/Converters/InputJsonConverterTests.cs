using System.Text.Json;
using Elsa.Expressions.Contracts;
using Elsa.Expressions.Models;
using Elsa.Workflows.Models;
using Elsa.Workflows.Serialization.Converters;
using Microsoft.Extensions.DependencyInjection;
using TUnit.Mocks;

namespace Elsa.Workflows.Core.UnitTests.Serialization.Converters;

public sealed class InputJsonConverterTests
{
    [Test]
    public async Task When_DeserializeUnknownExpressionType_Then_ThrowsJsonException()
    {
        // Arrange
        var options = CreateOptions();
        const string json = """{"typeName":"System.Boolean","expression":{"type":"JavaScript","value":"getVariable('x') > 0"}}""";

        // Act
        var exception = Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<Input<bool>>(json, options));

        // Assert
        await Assert.That(exception.Message).Contains("JavaScript");
    }

    [Test]
    public async Task When_SerializeUnknownExpressionType_Then_ThrowsJsonException()
    {
        // Arrange
        var options = CreateOptions();
        var input = new Input<bool>(new Expression("JavaScript", "getVariable('x') > 0"));

        // Act
        var exception = Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Serialize(input, options));

        // Assert
        await Assert.That(exception.Message).Contains("JavaScript");
    }

    [Test]
    public async Task When_DeserializeKnownExpressionType_Then_ReturnsInput()
    {
        // Arrange
        var options = CreateOptions(CreateLiteralDescriptor());
        const string json = """{"typeName":"System.Boolean","expression":{"type":"Literal","value":true}}""";

        // Act
        var result = JsonSerializer.Deserialize<Input<bool>>(json, options);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result.Expression).IsNotNull();
        await Assert.That(result.Expression.Type).IsEqualTo("Literal");
        await Assert.That((bool)result.Expression.Value!).IsTrue();
    }

    [Test]
    public async Task When_DeserializeInputObjectWithoutExpressionType_Then_ReturnsNull()
    {
        // Arrange
        var options = CreateOptions();
        const string json = """{"typeName":"System.Boolean"}""";

        // Act
        var result = JsonSerializer.Deserialize<Input<bool>>(json, options);

        // Assert
        await Assert.That(result).IsNull();
    }

    private static JsonSerializerOptions CreateOptions(ExpressionDescriptor? descriptor = null)
    {
        var registry = Mock.Of<IExpressionDescriptorRegistry>().Object;

        if (descriptor != null)
            Mock.Get(registry).Find(descriptor.Type).Returns(descriptor);

        var serviceProvider = new ServiceCollection()
            .AddSingleton(registry)
            .BuildServiceProvider();

        return new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            Converters =
            {
                new ExpressionJsonConverterFactory(registry),
                new InputJsonConverterFactory(serviceProvider)
            }
        };
    }

    private static ExpressionDescriptor CreateLiteralDescriptor() => new()
    {
        Type = "Literal"
    };
}
