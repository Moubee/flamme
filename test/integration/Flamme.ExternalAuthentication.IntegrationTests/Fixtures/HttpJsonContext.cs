using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.ExternalAuthentication.IntegrationTests.Connections;
using Elsa.ExternalAuthentication.IntegrationTests.Links;
using Elsa.ExternalAuthentication.Models;

namespace Elsa.ExternalAuthentication.IntegrationTests.Fixtures;

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
