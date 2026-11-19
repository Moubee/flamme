using System.Dynamic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Flamme.Workflows.IntegrationTests.Serialization.Polymorphism;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(Model))]
[JsonSerializable(typeof(List<Model>))]
[JsonSerializable(typeof(HashSet<Model>))]
[JsonSerializable(typeof(Dictionary<string, Model>))]
[JsonSerializable(typeof(CustomDictionary))]
[JsonSerializable(typeof(ExpandoObject))]
[JsonSerializable(typeof(IDictionary<string, object>))]
[JsonSerializable(typeof(object))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(Tests.MyNumber))]
internal partial class PolymorphismJsonContext : JsonSerializerContext;
