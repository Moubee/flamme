using System.Text.Json;
using Flamme.Http.Bookmarks;

namespace Flamme.Http.UnitTests.Bookmarks;

public class HttpEndpointBookmarkPayloadTests
{
    [Test]
    public async Task Constructor_NormalizesPathAndMethodForRecordEquality()
    {
        var payload = new HttpEndpointBookmarkPayload("/API/Orders", "GET");
        var normalizedPayload = new HttpEndpointBookmarkPayload("/api/orders", "get");

        await Assert.That(payload.Path).IsEqualTo("/api/orders");
        await Assert.That(payload.Method).IsEqualTo("get");
        await Assert.That(payload).IsEqualTo(normalizedPayload);
        await Assert.That(payload.GetHashCode()).IsEqualTo(normalizedPayload.GetHashCode());
    }

    [Test]
    public async Task RecordCopy_NormalizesUpdatedPathAndMethod()
    {
        var original = new HttpEndpointBookmarkPayload("/api/orders", "get");

        var updated = original with { Path = "/API/Customers", Method = "POST" };

        await Assert.That(updated.Path).IsEqualTo("/api/customers");
        await Assert.That(updated.Method).IsEqualTo("post");
        await Assert.That(original.Path).IsEqualTo("/api/orders");
        await Assert.That(original.Method).IsEqualTo("get");
    }

    [Test]
    public async Task JsonRoundTrip_PreservesNormalizedRoutingAndRequestOptions()
    {
        var payload = new HttpEndpointBookmarkPayload("/API/Orders", "POST", true, "orders", TimeSpan.FromSeconds(30), 1024);

        var json = JsonSerializer.Serialize(payload, TestJsonSerializerContext.Default.HttpEndpointBookmarkPayload);
        var restored = JsonSerializer.Deserialize(json, TestJsonSerializerContext.Default.HttpEndpointBookmarkPayload);

        await Assert.That(restored).IsEqualTo(payload);
        using var document = JsonDocument.Parse(json);
        await Assert.That(document.RootElement.GetProperty("Path").GetString()).IsEqualTo("/api/orders");
        await Assert.That(document.RootElement.GetProperty("Method").GetString()).IsEqualTo("post");
        await Assert.That(document.RootElement.EnumerateObject().Count()).IsEqualTo(6);
    }
}
