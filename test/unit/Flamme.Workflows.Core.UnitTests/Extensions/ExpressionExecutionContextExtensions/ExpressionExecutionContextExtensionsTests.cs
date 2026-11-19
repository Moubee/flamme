using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Flamme.Common;
using Flamme.Expressions.JavaScript.Activities;
using Flamme.Expressions.Models;
using Flamme.Extensions;
using Flamme.Testing.Shared;
using Flamme.Workflows.Activities;
using Flamme.Workflows.Memory;
using Microsoft.Extensions.DependencyInjection;
using TUnit.Mocks;

namespace Flamme.Workflows.Core.UnitTests;

public class ExpressionExecutionContextExtensionsTests
{
    [Test]
    public async Task GetVariable_ReturnsVariable_WhenVariableExists()
    {
        // Arrange
        var variable = new Variable("test", 5);
        var memoryRegister = new MemoryRegister(new Dictionary<string, MemoryBlock>
        {
            { variable.Id, new(variable.Value, new VariableBlockMetadata(variable, typeof(object), true)) }
        });

        var context = new ExpressionExecutionContext(null!, memoryRegister);

        // Act
        var result = context.GetVariable<int>("test");

        // Assert
        await Assert.That(result).IsEqualTo(5);
    }

    [Test]
    public async Task GetVariable_ReturnsNull_WhenVariableDoesNotExist()
    {
        // Arrange
        var memoryRegister = new MemoryRegister(new Dictionary<string, MemoryBlock>());
        var context = new ExpressionExecutionContext(null!, memoryRegister);

        // Act
        var result = context.GetVariable<string>("nonexistent");

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public void CreateVariable_ThrowsException_WhenVariableExists()
    {
        // Arrange
        var variable = new Variable("test", 5);
        var memoryRegister = new MemoryRegister(new Dictionary<string, MemoryBlock>
        {
            { variable.Id, new(variable.Value, new VariableBlockMetadata(variable, typeof(object), true)) }
        });

        var context = new ExpressionExecutionContext(null!, memoryRegister);

        // Act & Assert
        Assert.ThrowsExactly<Exception>(() => context.CreateVariable("test", 10));
    }

    [Test]
    public async Task CreateVariable_CreatesVariable_WhenVariableDoesNotExist()
    {
        // Arrange
        var memoryRegister = new MemoryRegister(new Dictionary<string, MemoryBlock>());
        var context = new ExpressionExecutionContext(null!, memoryRegister);

        // Act
        context.CreateVariable("newVariable", 10);

        // Assert
        var variable = context.GetVariable<int>("newVariable");
        await Assert.That(variable).IsEqualTo(10);
    }

    [Test]
    public async Task SetVariable_CreatesVariable_WhenVariableDoesNotExist()
    {
        // Arrange
        var memoryRegister = new MemoryRegister(new Dictionary<string, MemoryBlock>());
        var context = new ExpressionExecutionContext(null!, memoryRegister);

        // Act
        context.SetVariable("newVariable", 10);

        // Assert
        var variable = context.GetVariable<int>("newVariable");
        await Assert.That(variable).IsEqualTo(10);
    }

    [Test]
    public async Task SetVariable_SetsValue_WhenVariableExists()
    {
        // Arrange
        var variable = new Variable("test", 5);
        var memoryRegister = new MemoryRegister(new Dictionary<string, MemoryBlock>
        {
            { variable.Id, new(variable.Value, new VariableBlockMetadata(variable, typeof(object), true)) }
        });

        var context = new ExpressionExecutionContext(null!, memoryRegister);

        // Act
        context.SetVariable("test", 10);

        // Assert
        var updatedVariable = context.GetVariable<int>("test");
        await Assert.That(updatedVariable).IsEqualTo(10);
    }

    [Test]
    public async Task GetInput_UsesSecondHostConverters_AfterFirstHostAlreadyConverted()
    {
        // Arrange
        var firstContext = await CreateContextWithSerializerAsync("first");
        firstContext.WorkflowExecutionContext.Input["payload"] = """{"token":"x"}""";

        // Act
        var first = firstContext.ExpressionExecutionContext.GetInput<TaggedValue>("payload");

        var secondContext = await CreateContextWithSerializerAsync("second");
        secondContext.WorkflowExecutionContext.Input["payload"] = """{"token":"x"}""";
        var second = secondContext.ExpressionExecutionContext.GetInput<TaggedValue>("payload");

        // Assert
        await Assert.That(first?.Tag).IsEqualTo("first");
        await Assert.That(second?.Tag).IsEqualTo("second");
    }

    [Test]
    public async Task GetInput_ResolvesSerializerOptionsFromCurrentHostOnEachCall()
    {
        // Arrange
        var serializer = IJsonSerializer.Mock().Object;
        Mock.Get(serializer).GetOptions().ReturnsSequentially(CreateTaggedOptions("first"), CreateTaggedOptions("second"));

        var context = await CreateContextAsync(serializer);
        context.WorkflowExecutionContext.Input["payload"] = """{"token":"x"}""";

        // Act
        var first = context.ExpressionExecutionContext.GetInput<TaggedValue>("payload");
        var second = context.ExpressionExecutionContext.GetInput<TaggedValue>("payload");

        // Assert
        await Assert.That(first?.Tag).IsEqualTo("first");
        await Assert.That(second?.Tag).IsEqualTo("second");
    }

    private static Task<ActivityExecutionContext> CreateContextWithSerializerAsync(string converterTag) =>
        CreateContextAsync(new StubJsonSerializer(CreateTaggedOptions(converterTag)));

    private static Task<ActivityExecutionContext> CreateContextAsync(IJsonSerializer serializer)
    {
        var fixture = new ActivityTestFixture(new WriteLine("test"))
            .ConfigureServices(services => services.AddSingleton(serializer));
        return fixture.BuildAsync();
    }

    private static JsonSerializerOptions CreateTaggedOptions(string tag)
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new TaggedValueConverter(tag));
        return options;
    }

    private sealed class TaggedValue
    {
        public string Tag { get; set; } = "";
    }

    private sealed class TaggedValueConverter(string tag) : JsonConverter<TaggedValue>
    {
        public override TaggedValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var _ = JsonDocument.ParseValue(ref reader);
            return new TaggedValue { Tag = tag };
        }

        public override void Write(Utf8JsonWriter writer, TaggedValue value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.Tag);
    }

    private sealed class StubJsonSerializer(JsonSerializerOptions options) : IJsonSerializer
    {
        public JsonSerializerOptions GetOptions() => options;
        public void ApplyOptions(JsonSerializerOptions _) { }
        [RequiresUnreferencedCode("Matches the reflection-based IJsonSerializer contract.")]
        public string Serialize(object value) => throw new NotSupportedException();
        [RequiresUnreferencedCode("Matches the reflection-based IJsonSerializer contract.")]
        public string Serialize(object value, Type type) => throw new NotSupportedException();
        public string Serialize<T>(T value) => throw new NotSupportedException();
        [RequiresUnreferencedCode("Matches the reflection-based IJsonSerializer contract.")]
        public object Deserialize(string json) => throw new NotSupportedException();
        [RequiresUnreferencedCode("Matches the reflection-based IJsonSerializer contract.")]
        public object Deserialize(string json, Type type) => throw new NotSupportedException();
        public T Deserialize<T>(string json) => throw new NotSupportedException();
    }
}
