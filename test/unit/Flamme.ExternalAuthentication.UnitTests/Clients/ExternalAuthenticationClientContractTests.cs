using System.Text.Json;
using System.Text.Json.Serialization;
using Flamme.Api.Client.Resources.ExternalAuthentication.Connections.Models;
using Flamme.Api.Client.Resources.ExternalAuthentication.Connections.Requests;
using System.Threading.Tasks;

namespace Flamme.ExternalAuthentication.UnitTests.Clients;

public class ExternalAuthenticationClientContractTests
{
    [Test]
    public async Task NewSaveRequestSerializesHostScope()
    {
        var request = new SaveExternalAuthenticationConnectionRequest
        {
            AdapterSettings = JsonElement.Parse("{}")
        };
        var json = JsonSerializer.Serialize(request, ClientContractJsonContext.Default.SaveExternalAuthenticationConnectionRequest);
        using var document = JsonDocument.Parse(json);

        await Assert.That(document.RootElement.GetProperty("scope").GetProperty("kind").GetString()).IsEqualTo("host");
    }

    [Test]
    public async Task ConnectionDeserializesNamedShadowRelationships()
    {
        var connection = JsonSerializer.Deserialize(
            """
            {
              "id": "deployment-keycloak",
              "shadowed": true,
              "shadowedBy": {
                "id": "database-keycloak",
                "displayName": "Keycloak",
                "source": "database"
              },
              "shadows": []
            }
            """,
            ClientContractJsonContext.Default.ExternalAuthenticationConnection);

        await Assert.That(connection).IsNotNull();
        var deserializedConnection = connection!;
        await Assert.That(deserializedConnection.ShadowedBy?.Id).IsEqualTo("database-keycloak");
        await Assert.That(deserializedConnection.ShadowedBy?.DisplayName).IsEqualTo("Keycloak");
        await Assert.That(deserializedConnection.ShadowedBy?.Source).IsEqualTo("database");
        await Assert.That(deserializedConnection.Shadows).IsEmpty();
    }
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(SaveExternalAuthenticationConnectionRequest))]
[JsonSerializable(typeof(ExternalAuthenticationConnection))]
internal partial class ClientContractJsonContext : JsonSerializerContext;
