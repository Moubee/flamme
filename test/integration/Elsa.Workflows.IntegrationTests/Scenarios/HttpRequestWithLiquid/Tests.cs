using System.Text.Json;
using Elsa.Extensions;
using Elsa.Testing.Shared;
using Elsa.Workflows.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.IntegrationTests.Scenarios.HttpRequestWithLiquid;

public sealed class Tests : IAsyncDisposable
{
    private readonly IWorkflowRunner _workflowRunner;
    private readonly CapturingTextWriter _capturingTextWriter = new();
    private readonly IServiceProvider _services;

    public Tests()
    {
        _services = new TestApplicationBuilder(TestContext.Current!.Output.StandardOutput)
            .WithCapturingTextWriter(_capturingTextWriter)
            .ConfigureElsa(configure => configure.UseHttp())
            .Build();
        _workflowRunner = _services.GetRequiredService<IWorkflowRunner>();
    }

    [Test]
    public async Task GetProducts()
    {
        await _services.PopulateRegistriesAsync();
        RunWorkflowResult result = await _workflowRunner.RunAsync(new JavascriptAndLiquidWorkflow());
        await Assert.That(result.WorkflowState.Status).IsEqualTo(WorkflowStatus.Finished);
        await Assert.That(result.WorkflowState.Incidents).IsEmpty();
        await Assert.That(result.WorkflowState.SubStatus).IsEqualTo(WorkflowSubStatus.Finished);

        var lines = _capturingTextWriter.Lines.ToList();
        await Assert.That(lines).Contains("First product id: 1");
        await Assert.That(lines).Contains("First product price rounded: 13");
        // .NET 11 retains more precision when Fluid converts a JavaScript double
        // to decimal. Check the JSON value rather than its runtime-specific spelling.
        const string productJsonPrefix = "First product as json: ";
        var productJsonLine = lines.Single(x => x.StartsWith(productJsonPrefix, StringComparison.Ordinal));
        using var productJson = JsonDocument.Parse(productJsonLine[productJsonPrefix.Length..]);
        await Assert.That(productJson.RootElement.GetProperty("id").GetInt32()).IsEqualTo(1);
        await Assert.That(productJson.RootElement.GetProperty("price").GetDouble()).IsEqualTo(12.99);
        await Assert.That(lines).Contains("Second product id: 2");

        await Assert.That(lines).Contains("Single product id: 2");
        await Assert.That(lines).Contains("Single product as json: {\"id\":2,\"price\":10}");
    }

    public async ValueTask DisposeAsync()
    {
        await TestResourceDisposal.DisposeAsync(_services);
        _capturingTextWriter.Dispose();
        GC.SuppressFinalize(this);
    }
}
