using System.Text;
using System.Text.Json.Nodes;
using System.Net;
using System.Net.Http.Json;
using Flamme.Alterations.Core.Contracts;
using Flamme.Common;
using Flamme.Workflows;
using Flamme.Workflows.Management;
using Flamme.Workflows.Management.Enums;
using Flamme.Workflows.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using TUnit.AspNetCore;

namespace Flamme.Alterations.IntegrationTests;

public class AlterationsApiTimestampFilterTests : WebApplicationTest<AlterationsWebApplicationFactory, AlterationsTestEntryPoint>
{
    private HttpClient? _httpClient;
    private HttpClient HttpClient
    {
        get
        {
            if (_httpClient != null)
                return _httpClient;

            _httpClient = Factory.CreateClient();
            _httpClient.DefaultRequestHeaders.Add(AlterationsTestAuthenticationHandler.PermissionHeader, "*");
            return _httpClient;
        }
    }

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.AddSingleton(IWorkflowInstanceFinder.Mock().Object);
        services.AddSingleton(IAlterationPlanScheduler.Mock().Object);
        services.AddSingleton(IAlterationRunner.Mock().Object);
        services.AddSingleton(IAlteredWorkflowDispatcher.Mock().Object);
        services.AddSingleton(IAlterationPlanStore.Mock().Object);
        services.AddSingleton(IAlterationJobStore.Mock().Object);
        services.AddSingleton(IWorkflowDispatcher.Mock().Object);
        services.AddSingleton(IWorkflowInstanceStore.Mock().Object);
        services.AddSingleton(IIdentityGenerator.Mock().Object);
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(DateTimeOffset.UnixEpoch));
    }

    [Test]
    [Arguments("/alterations/dry-run")]
    [Arguments("/alterations/submit")]
    public async Task Post_WithInjectedTimestampFilterColumn_ReturnsBadRequest(string path)
    {
        var response = await HttpClient.PostAsync(path, CreateJsonContent(CreateRequest(path)));
        var body = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(body).Contains("Invalid timestamp filter column", StringComparison.CurrentCulture);
    }

    [Test]
    public async Task Submit_WithNullFilter_DoesNotReturnServerError()
    {
        var response = await HttpClient.PostAsync("/alterations/submit", CreateJsonContent(new JsonObject { ["filter"] = null }));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    [Arguments("/alterations/dry-run")]
    [Arguments("/alterations/submit")]
    public async Task Post_WithNullTimestampFilter_ReturnsBadRequest(string path)
    {
        var response = await HttpClient.PostAsync(path, CreateJsonContent(CreateRequestWithNullTimestampFilter(path)));
        var body = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(body).Contains("Timestamp filter at index 0 must be specified.", StringComparison.CurrentCulture);
    }

    [Test]
    [Arguments("/alterations/dry-run")]
    [Arguments("/alterations/submit")]
    public async Task Post_WithMultipleInvalidTimestampFilters_ReturnsAllValidationErrors(string path)
    {
        var response = await HttpClient.PostAsync(path, CreateJsonContent(CreateRequestWithMultipleInvalidTimestampFilters(path)));
        var body = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(body).Contains("Timestamp filter at index 0: Timestamp filter column must be specified.", StringComparison.CurrentCulture);
        await Assert.That(body).Contains("Timestamp filter at index 1 must be specified.", StringComparison.CurrentCulture);
    }

    private static StringContent CreateJsonContent(JsonObject request) =>
        new(request.ToJsonString(), Encoding.UTF8, "application/json");

    private static JsonObject WrapFilter(string path, JsonObject filter) =>
        path == "/alterations/submit" ? new JsonObject { ["filter"] = filter } : filter;

    private static JsonObject CreateRequest(string path) => WrapFilter(path, new JsonObject
    {
        ["timestampFilters"] = new JsonArray(new JsonObject
        {
            ["column"] = "CreatedAt == @0 || Id != null",
            ["operator"] = (int)TimestampFilterOperator.Is,
            ["timestamp"] = new DateTimeOffset(2026, 5, 20, 10, 0, 0, TimeSpan.Zero)
        })
    });

    private static JsonObject CreateRequestWithNullTimestampFilter(string path) => WrapFilter(path, new JsonObject
    {
        ["timestampFilters"] = new JsonArray((JsonNode?)null)
    });

    private static JsonObject CreateRequestWithMultipleInvalidTimestampFilters(string path) => WrapFilter(path, new JsonObject
    {
        ["timestampFilters"] = new JsonArray(new JsonObject
        {
            ["column"] = " ",
            ["operator"] = (int)TimestampFilterOperator.Is,
            ["timestamp"] = new DateTimeOffset(2026, 5, 20, 10, 0, 0, TimeSpan.Zero)
        }, null)
    });
}
