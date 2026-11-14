using System.Net;
using System.Net.Http.Json;
using Elsa.Alterations.Core.Contracts;
using Elsa.Common;
using Elsa.Workflows;
using Elsa.Workflows.Management;
using Elsa.Workflows.Management.Enums;
using Elsa.Workflows.Runtime;
using Microsoft.Extensions.DependencyInjection;
using TUnit.AspNetCore;

namespace Elsa.Alterations.IntegrationTests;

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
        services.AddSingleton(Mock.Of<IWorkflowInstanceFinder>().Object);
        services.AddSingleton(Mock.Of<IAlterationPlanScheduler>().Object);
        services.AddSingleton(Mock.Of<IAlterationRunner>().Object);
        services.AddSingleton(Mock.Of<IAlteredWorkflowDispatcher>().Object);
        services.AddSingleton(Mock.Of<IAlterationPlanStore>().Object);
        services.AddSingleton(Mock.Of<IAlterationJobStore>().Object);
        services.AddSingleton(Mock.Of<IWorkflowDispatcher>().Object);
        services.AddSingleton(Mock.Of<IWorkflowInstanceStore>().Object);
        services.AddSingleton(Mock.Of<IIdentityGenerator>().Object);
        services.AddSingleton(Mock.Of<ISystemClock>().Object);
    }

    [Test]
    [Arguments("/alterations/dry-run")]
    [Arguments("/alterations/submit")]
    public async Task Post_WithInjectedTimestampFilterColumn_ReturnsBadRequest(string path)
    {
        var response = await HttpClient.PostAsJsonAsync(path, CreateRequest(path));
        var body = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(body).Contains("Invalid timestamp filter column", StringComparison.CurrentCulture);
    }

    [Test]
    public async Task Submit_WithNullFilter_DoesNotReturnServerError()
    {
        var response = await HttpClient.PostAsJsonAsync("/alterations/submit", new { filter = (object?)null });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    [Arguments("/alterations/dry-run")]
    [Arguments("/alterations/submit")]
    public async Task Post_WithNullTimestampFilter_ReturnsBadRequest(string path)
    {
        var response = await HttpClient.PostAsJsonAsync(path, CreateRequestWithNullTimestampFilter(path));
        var body = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(body).Contains("Timestamp filter at index 0 must be specified.", StringComparison.CurrentCulture);
    }

    [Test]
    [Arguments("/alterations/dry-run")]
    [Arguments("/alterations/submit")]
    public async Task Post_WithMultipleInvalidTimestampFilters_ReturnsAllValidationErrors(string path)
    {
        var response = await HttpClient.PostAsJsonAsync(path, CreateRequestWithMultipleInvalidTimestampFilters(path));
        var body = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(body).Contains("Timestamp filter at index 0: Timestamp filter column must be specified.", StringComparison.CurrentCulture);
        await Assert.That(body).Contains("Timestamp filter at index 1 must be specified.", StringComparison.CurrentCulture);
    }

    private static object CreateRequest(string path)
    {
        var filter = new
        {
            timestampFilters = new[]
            {
                new
                {
                    column = "CreatedAt == @0 || Id != null",
                    @operator = TimestampFilterOperator.Is,
                    timestamp = new DateTimeOffset(2026, 5, 20, 10, 0, 0, TimeSpan.Zero)
                }
            }
        };

        return path == "/alterations/submit"
            ? new { filter }
            : filter;
    }

    private static object CreateRequestWithNullTimestampFilter(string path)
    {
        var filter = new
        {
            timestampFilters = new object?[] { null }
        };

        return path == "/alterations/submit"
            ? new { filter }
            : filter;
    }

    private static object CreateRequestWithMultipleInvalidTimestampFilters(string path)
    {
        var filter = new
        {
            timestampFilters = new object?[]
            {
                new
                {
                    column = " ",
                    @operator = TimestampFilterOperator.Is,
                    timestamp = new DateTimeOffset(2026, 5, 20, 10, 0, 0, TimeSpan.Zero)
                },
                null
            }
        };

        return path == "/alterations/submit"
            ? new { filter }
            : filter;
    }
}
