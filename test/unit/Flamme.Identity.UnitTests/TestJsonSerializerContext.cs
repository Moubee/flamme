using System.Text.Json.Serialization;

namespace Flamme.Identity.UnitTests;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(global::Flamme.Identity.Models.LoginResponse), TypeInfoPropertyName = "LoginResponse")]
[JsonSerializable(typeof(global::Flamme.Identity.UnitTests.Endpoints.EmptyPermissionsClaimTests.MePermissionsResponse), TypeInfoPropertyName = "MePermissionsResponse")]
internal partial class TestJsonSerializerContext : JsonSerializerContext;
