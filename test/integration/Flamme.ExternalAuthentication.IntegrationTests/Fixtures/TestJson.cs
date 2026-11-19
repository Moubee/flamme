using System.Text;
using System.Text.Json;

namespace Elsa.ExternalAuthentication.IntegrationTests.Fixtures;

internal static class TestJson
{
    public static JsonElement Element(JsonNode value) => JsonElement.Parse(value.ToJsonString());

    public static StringContent Content(JsonNode value) => new(value.ToJsonString(), Encoding.UTF8, "application/json");

    public static async Task<HttpResponseMessage> PostJsonAsync(this HttpClient client, string path, JsonNode value)
    {
        using var content = Content(value);
        return await client.PostAsync(path, content);
    }
}
