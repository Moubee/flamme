using System.Text.Json;
using System.Text.Json.Serialization;
using Flamme.ExternalAuthentication.IntegrationTests.Connections;
using Flamme.ExternalAuthentication.IntegrationTests.Links;
using Flamme.ExternalAuthentication.Models;

namespace Flamme.ExternalAuthentication.IntegrationTests.Fixtures;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(PolicySelection))]
[JsonSerializable(typeof(ConnectionManagementTests.ConnectionDocument), TypeInfoPropertyName = "ConnectionDocument")]
[JsonSerializable(typeof(ConnectionManagementTests.ListDocument), TypeInfoPropertyName = "ListDocument")]
[JsonSerializable(typeof(ConnectionManagementTests.ListConnectionDocument), TypeInfoPropertyName = "ListConnectionDocument")]
[JsonSerializable(typeof(ExternalIdentityLinkTests.LinkDocument), TypeInfoPropertyName = "LinkDocument")]
[JsonSerializable(typeof(ExternalIdentityLinkTests.LinkList), TypeInfoPropertyName = "LinkList")]
[JsonSerializable(typeof(ExternalIdentityLinkTests.ErrorDocument), TypeInfoPropertyName = "ErrorDocument")]
[JsonSerializable(typeof(ExternalIdentityLinkTests.UserList), TypeInfoPropertyName = "UserList")]
internal partial class HttpJsonContext : JsonSerializerContext;
