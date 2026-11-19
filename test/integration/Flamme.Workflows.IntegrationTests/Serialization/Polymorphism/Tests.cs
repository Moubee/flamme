using System.Diagnostics.CodeAnalysis;
using System.Dynamic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Flamme.Workflows.Options;
using Flamme.Workflows.Serialization.Converters;
using Flamme.Workflows.Serialization.ReferenceHandlers;
using Flamme.Workflows.Services;
using Flamme.Common.Serialization;

namespace Flamme.Workflows.IntegrationTests.Serialization.Polymorphism;

public class Tests
{
    [Test]
    [DisplayName("Objects mixed with primitive, complex, expando objects and arrays of these can be serialized")]
    public async Task Test1()
    {
        var model = CreateModel();
        var expectedJson = File.ReadAllText("Serialization/Polymorphism/data.json").TrimEnd();
        var actualJson = JsonSerializer.Serialize(model, GetModelTypeInfo());
        await Assert.That(actualJson).IsEqualTo(expectedJson);
    }

    [Test]
    [DisplayName("Objects mixed with primitive, complex, expando objects and arrays of these can be round-tripped")]
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Preserves the legacy polymorphic converter regression over an init-only record with cyclic references; .NET 10 generated metadata cannot restore this shape through its constructor.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "The existing init-only reference-preserving converter path is a JIT regression boundary until production serializer metadata supports it.")]
    public async Task Test2()
    {
        var model = CreateModel();
        var json = JsonSerializer.Serialize(model, GetSerializerOptions(useSourceGeneration: false));
        var deserializedModel = JsonSerializer.Deserialize<Model>(json, GetSerializerOptions(useSourceGeneration: false));
        var roundTrippedJson = JsonSerializer.Serialize(deserializedModel, GetSerializerOptions(useSourceGeneration: false));
        await Assert.That(roundTrippedJson).IsEqualTo(json);
    }

    private Model CreateModel()
    {
        // Create a model with various nested properties
        var metadata = new ExpandoObject() as IDictionary<string, object>;

        // Initialize metadata with test data.
        metadata["Foo"] = "Bar";
        metadata["Number"] = 123;
        metadata["Flag"] = true;
        metadata["Models"] = new List<Model>
        {
            new(Text: "Hello", Number: 1, Flag: true, Metadata: metadata),
            new(Text: "World", Number: 2)
        };
        metadata["CustomDictionary"] = new CustomDictionary { ["content-type"] = new[] { "application/json" } };

        return new Model(
            "Hello World",
            123,
            true,
            new List<Model>
            {
                new("Hello", 1, true, new List<Model>() { new(Metadata: metadata) }),
                new("World", 2)
            },
            metadata,
            new Model("Payload"),
            new HashSet<Model>
            {
                new()
                {
                    Text = "I'm a model in a set!"
                }
            },
            new Dictionary<string, Model>
            {
                ["Hello"] = new("Hello", 1, true),
                ["World"] = new("World", 2),
            }
        );
    }

    private static JsonTypeInfo<Model> GetModelTypeInfo()
    {
        var options = GetSerializerOptions();
        return (JsonTypeInfo<Model>)options.GetTypeInfo(typeof(Model));
    }

    private static JsonSerializerOptions GetSerializerOptions(bool useSourceGeneration = true)
    {
        var referenceHandler = new CrossScopedReferenceHandler();
        var workflowJsonTypeRegistry = new SerializationTypeRegistry(Microsoft.Extensions.Options.Options.Create(new SerializationTypeOptions()));
        workflowJsonTypeRegistry.RegisterType(typeof(Model), nameof(Model));
        workflowJsonTypeRegistry.RegisterType(typeof(CustomDictionary), nameof(CustomDictionary));

        var options = new JsonSerializerOptions
        {
            TypeInfoResolver = useSourceGeneration ? PolymorphismJsonContext.Default : null,
            ReferenceHandler = referenceHandler,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        options.Converters.Add(JsonMetadataServices.TimeSpanConverter);
        options.Converters.Add(new PolymorphicObjectConverterFactory(workflowJsonTypeRegistry));
        return options;
    }

    [Test]
    [DisplayName("Types with custom converters that serialize to primitives are serialized as primitives")]
    
    public async Task CustomConverterProducingPrimitive_IsSerializedAsPrimitive()
    {
        var model = new MyNumber { Number = 123UL };
        var options = GetSerializerOptions();
        var expectedJson = "123";
        var json = JsonSerializer.Serialize<object>(model, (JsonTypeInfo<object>)options.GetTypeInfo(typeof(object)));
        await Assert.That(json).IsEqualTo(expectedJson);
    }

    [JsonConverter(typeof(MyNumberConverter))]
    internal struct MyNumber
    {
        public ulong Number { get; init; }
    }

    internal class MyNumberConverter : JsonConverter<MyNumber>
    {
        public override MyNumber Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => new() { Number = reader.GetUInt64() };

        public override void Write(Utf8JsonWriter writer, MyNumber value, JsonSerializerOptions options)
            => writer.WriteNumberValue(value.Number);
    }
}
