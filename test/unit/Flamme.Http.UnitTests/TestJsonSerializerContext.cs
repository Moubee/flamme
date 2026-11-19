using System.Text.Json.Serialization;

namespace Flamme.Http.UnitTests;

[JsonSerializable(typeof(global::Flamme.Http.Bookmarks.HttpEndpointBookmarkPayload), TypeInfoPropertyName = "HttpEndpointBookmarkPayload")]
internal partial class TestJsonSerializerContext : JsonSerializerContext;
