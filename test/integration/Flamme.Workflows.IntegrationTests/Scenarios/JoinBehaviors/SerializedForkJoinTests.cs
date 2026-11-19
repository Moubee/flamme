using System.Text;
using System.Text.Json;
using Flamme.Testing.Shared;
using Flamme.Workflows.Activities.Flowchart.Extensions;
using Flamme.Workflows.Models;
using Flamme.Workflows.Management;
using Flamme.Workflows.Management.Models;
using Flamme.Workflows.Options;
using Flamme.Workflows.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace Flamme.Workflows.IntegrationTests.Scenarios.JoinBehaviors;

public class SerializedForkJoinTests
{
    [Test]
    public async Task ExportedWaitAnyJoinCancelsTheOtherBranchAndWritesOneReceipt()
    {
        await using var author = new WorkflowTestFixture(TestContext.Current!.Output.StandardOutput);
        var definition = await author.ImportWorkflowDefinitionAsync("Scenarios/JoinBehaviors/Workflows/capability-fork-join.json");
        var exporter = author.Services.GetRequiredService<IWorkflowDefinitionExporter>();
        var export = await exporter.ExportDefinitionAsync(definition);
        using var json = JsonDocument.Parse(export.Data);
        var join = json.RootElement.GetProperty("root").GetProperty("activities").EnumerateArray().Single(x => x.GetProperty("id").GetString() == "join");
        await Assert.That(join.GetProperty("type").GetString()).IsEqualTo("ControlFlow.Join");
        await Assert.That(join.GetProperty("version").GetInt32()).IsEqualTo(1);

        await using var consumer = new WorkflowTestFixture(TestContext.Current!.Output.StandardOutput);
        await consumer.BuildAsync();
        var serializer = consumer.Services.GetRequiredService<IActivitySerializer>();
        var model = serializer.Deserialize<WorkflowDefinitionModel>(Encoding.UTF8.GetString(export.Data));
        var importer = consumer.Services.GetRequiredService<IWorkflowDefinitionImporter>();
        var imported = await importer.ImportAsync(new() { Model = model, Publish = true });
        var runtime = consumer.Services.GetRequiredService<IWorkflowRuntime>();
        var client = await runtime.CreateClientAsync();
        await client.CreateAndRunInstanceAsync(new()
        {
            WorkflowDefinitionHandle = WorkflowDefinitionHandle.ByDefinitionVersionId(imported.WorkflowDefinition.Id),
            Properties = new RunWorkflowOptions().WithTokenBasedFlowchart().Properties
        });
        var state = await client.ExportStateAsync();

        await Assert.That(state.SubStatus).IsEqualTo(WorkflowSubStatus.Finished);
        await Assert.That(state.Incidents).IsEmpty();
        await Assert.That(state.Bookmarks).IsEmpty();
        await Assert.That(consumer.CapturingTextWriter.Lines).IsEquivalentTo(new[] { "ready", "Elsa.WriteLine" }, StringComparer.Ordinal, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }
}
