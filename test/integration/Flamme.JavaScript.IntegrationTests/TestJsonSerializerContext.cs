using System.Dynamic;
using System.Numerics;
using System.Text.Json.Serialization;

namespace Flamme.JavaScript.IntegrationTests;

[JsonSerializable(typeof(ExpandoObject))]
[JsonSerializable(typeof(BigInteger))]
internal partial class TestJsonSerializerContext : JsonSerializerContext;
