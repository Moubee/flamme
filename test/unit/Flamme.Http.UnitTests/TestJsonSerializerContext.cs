using System.Text.Json.Serialization;

namespace Elsa.Http.UnitTests;

[JsonSerializable(typeof(global::Elsa.Http.Bookmarks.HttpEndpointBookmarkPayload), TypeInfoPropertyName = "HttpEndpointBookmarkPayload")]
internal partial class TestJsonSerializerContext : JsonSerializerContext;
