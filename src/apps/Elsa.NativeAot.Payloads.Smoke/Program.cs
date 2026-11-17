using System.Text.Json;
using Elsa.Common.Serialization;
using Elsa.NativeAot;
using Elsa.Workflows.Serialization.Converters;
using Elsa.Workflows.Serialization.Serializers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

return await ProbeRunner.RunAsync(args, "payloads", () =>
{
    var expected = new Payload { Name = "sample \u03bb", Count = 3, Child = new Child { Label = "nested" } };
    Console.WriteLine("CHECK payloads.generated-metadata-control");
    var control = JsonSerializer.Serialize(expected, PayloadJsonContext.Default.Payload);
    var restoredControl = JsonSerializer.Deserialize(control, PayloadJsonContext.Default.Payload);
    ProbeRunner.Require(restoredControl is { Count: 3, Child.Label: "nested" } && restoredControl.Name == expected.Name,
        "Authored STJ metadata must round-trip without reflection JSON.");
    Console.WriteLine("PASS payloads.generated-metadata-control");

    var registry = SerializationTypeRegistry.CreateDefault();
    registry.RegisterType(typeof(Payload), "Payload");
    registry.RegisterType(typeof(Child), "Child");
    var services = new ServiceCollection();
    services.AddSingleton(new PolymorphicObjectConverterFactory(registry));
    services.AddSingleton(new TypeJsonConverter(registry));
    services.AddSingleton(new VariableConverterFactory(registry, NullLoggerFactory.Instance));
    using var provider = services.BuildServiceProvider();
    var serializer = new JsonPayloadSerializer(provider);

    Console.WriteLine("CHECK payloads.production-dto-round-trip");
    var json = serializer.Serialize(expected);
    using var document = JsonDocument.Parse(json);
    var body = document.RootElement;
    ProbeRunner.Require(body.GetProperty("_type").GetString() == "Payload", "The root DTO must carry its registered alias.");
    ProbeRunner.Require(!body.TryGetProperty("optional", out _), "Null DTO properties must be omitted.");
    ProbeRunner.Require(!body.GetProperty("child").TryGetProperty("_type", out _), "A typed child must remain an ordinary JSON body.");
    var restored = serializer.Deserialize(json);
    ProbeRunner.Require(restored is Payload dto && dto.Name == expected.Name && dto.Count == 3 && dto.Child.Label == "nested",
        "The production serializer must restore the aliased DTO and nested body.");
    ProbeRunner.Require(serializer.Deserialize<Payload>(serializer.SerializeToElement(expected)).Count == 3,
        "The typed element overload must restore the DTO body.");

    Console.WriteLine("CHECK payloads.production-list-and-scalars");
    var listJson = serializer.Serialize(new List<Payload> { expected });
    using var listDocument = JsonDocument.Parse(listJson);
    ProbeRunner.Require(listDocument.RootElement.GetProperty("_type").GetString() == "List<Payload>",
        "A root list must use its synthesized collection alias.");
    ProbeRunner.Require(serializer.Deserialize(listJson) is List<Payload> list && list.Count == 1 && list[0].Count == 3,
        "The untyped collection overload must restore its registered element type.");
    ProbeRunner.Require(serializer.Deserialize(serializer.Serialize(42)) is long number && number == 42,
        "An untyped integral scalar must restore as Int64.");
    ProbeRunner.Require(serializer.Deserialize(serializer.Serialize("text")) is string text && text == "text",
        "The string scalar must round-trip.");
    return Task.CompletedTask;
});
