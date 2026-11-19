using System.Text.Json.Serialization;

namespace Elsa.Alterations.Persistence.ConformanceTests;

[JsonSerializable(typeof(TestAlteration))]
[JsonSerializable(typeof(TestAlteration[]))]
internal partial class TestJsonSerializerContext : JsonSerializerContext;
