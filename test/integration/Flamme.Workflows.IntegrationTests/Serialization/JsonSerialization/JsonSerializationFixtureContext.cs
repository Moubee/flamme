using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Flamme.Workflows.IntegrationTests.Serialization.JsonSerialization;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(IDictionary<string, object>), TypeInfoPropertyName = "ObjectMap")]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(JsonObject))]
[JsonSerializable(typeof(JsonArray))]
internal partial class JsonSerializationFixtureContext : JsonSerializerContext;
