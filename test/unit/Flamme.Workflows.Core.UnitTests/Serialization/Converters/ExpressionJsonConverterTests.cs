using System.Text.Json;
using Elsa.Expressions.Contracts;
using Elsa.Expressions.Models;
using Elsa.Workflows.Serialization.Converters;
using TUnit.Mocks;

namespace Elsa.Workflows.Core.UnitTests.Serialization.Converters;

public sealed class ExpressionJsonConverterTests
{
    [Test]
    public async Task When_DeserializeUnknownExpressionType_Then_ThrowsJsonException()
    {
        // Arrange
        var options = CreateOptions();
        const string json = """{"type":"JavaScript","value":"getVariable('x') > 0"}""";

        // Act
        var exception = Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize(json, TestJsonTypeInfo.FromConverter<Expression>(options)));

        // Assert
        await Assert.That(exception.Message).Contains("JavaScript");
    }

    [Test]
    public async Task When_SerializeUnknownExpressionType_Then_ThrowsJsonException()
    {
        // Arrange
        var options = CreateOptions();
        var expression = new Expression("JavaScript", "getVariable('x') > 0");

        // Act
        var exception = Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Serialize(expression, TestJsonTypeInfo.FromConverter<Expression>(options)));

        // Assert
        await Assert.That(exception.Message).Contains("JavaScript");
    }

    [Test]
    public async Task When_DeserializeKnownExpressionType_Then_ReturnsExpression()
    {
        // Arrange
        var options = CreateOptions(CreateLiteralDescriptor());
        const string json = """{"type":"Literal","value":"hello"}""";

        // Act
        var result = JsonSerializer.Deserialize(json, TestJsonTypeInfo.FromConverter<Expression>(options));

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result.Type).IsEqualTo("Literal");
        await Assert.That(result.Value).IsEqualTo("hello");
    }

    [Test]
    [Arguments("{}")]
    [Arguments("{\"type\":\"\"}")]
    public async Task When_DeserializeExpressionWithoutOrWithEmptyType_Then_ReturnsNull(string json)
    {
        // Arrange
        var options = CreateOptions();

        // Act
        var result = JsonSerializer.Deserialize(json, TestJsonTypeInfo.FromConverter<Expression>(options));

        // Assert
        await Assert.That(result).IsNull();
    }

    private static JsonSerializerOptions CreateOptions(ExpressionDescriptor? descriptor = null)
    {
        var registry = IExpressionDescriptorRegistry.Mock().Object;

        if (descriptor != null)
            Mock.Get(registry).Find(descriptor.Type).Returns(descriptor);

        return new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            Converters =
            {
                new ExpressionJsonConverterFactory(registry)
            }
        };
    }

    private static ExpressionDescriptor CreateLiteralDescriptor() => new()
    {
        Type = "Literal"
    };
}
