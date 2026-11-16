using System.Text.Json.Serialization;

namespace Elsa.Identity.UnitTests;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(global::Elsa.Identity.Models.LoginResponse), TypeInfoPropertyName = "LoginResponse")]
[JsonSerializable(typeof(global::Elsa.Identity.UnitTests.Endpoints.EmptyPermissionsClaimTests.MePermissionsResponse), TypeInfoPropertyName = "MePermissionsResponse")]
internal partial class TestJsonSerializerContext : JsonSerializerContext;
