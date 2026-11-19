using Flamme.Common.Models;
using Flamme.Extensions;
using Flamme.Testing.Shared;
using Flamme.Workflows.Activities;
using Flamme.Workflows.Management;
using Flamme.Workflows.Management.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Flamme.Workflows.IntegrationTests.Scenarios.ImportAndPublish;

public class UnresolvedContractTests
{
    private const string Source = """
        {
          "id": "wrong-major-v1", "definitionId": "wrong-major", "name": "Wrong major",
          "root": {
            "type": "ControlFlow.Sequence", "version": 1, "id": "root",
            "activities": [
              { "type": "Console.WriteLine", "version": 1, "id": "first", "text": { "typeName": "String", "expression": { "type": "Literal", "value": "first effect" } } },
              { "type": "Console.WriteLine", "version": 42, "id": "unresolved" }
            ]
          }
        }
        """;

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ImportReportsUnresolvedOccurrenceBeforePublishing(bool allowValidationErrors)
    {
        await using var host = new WorkflowTestFixture(TestContext.Current!.Output.StandardOutput);
        if (allowValidationErrors)
            host.ConfigureFlamme(f => f.UseWorkflowManagement(m => m.UseFailOnValidationErrors(false)));
        await host.BuildAsync();
        var serializer = host.Services.GetRequiredService<IActivitySerializer>();
        var importer = host.Services.GetRequiredService<IWorkflowDefinitionImporter>();
        var definitions = host.Services.GetRequiredService<IWorkflowDefinitionService>();
        var model = serializer.Deserialize<WorkflowDefinitionModel>(Source);

        var result = await importer.ImportAsync(new() { Model = model, Publish = true });

        await Assert.That(result.Succeeded).IsEqualTo(allowValidationErrors);
        var error = await Assert.That(result.ValidationErrors).HasSingleItem();
        await Assert.That(error.ActivityId).IsEqualTo("unresolved");
        await Assert.That(error.Message).Contains("Console.WriteLine");
        await Assert.That(error.Message).Contains("42");
        var published = await definitions.FindWorkflowDefinitionAsync("wrong-major", VersionOptions.Published);
        await Assert.That(published != null).IsEqualTo(allowValidationErrors);
        await Assert.That(host.CapturingTextWriter.Lines).IsEmpty();
    }

    [Test]
    public async Task UnresolvedOccurrenceCanRoundTripAsAnEditableDraft()
    {
        await using var host = new WorkflowTestFixture(TestContext.Current!.Output.StandardOutput);
        await host.BuildAsync();
        var serializer = host.Services.GetRequiredService<IActivitySerializer>();
        var importer = host.Services.GetRequiredService<IWorkflowDefinitionImporter>();
        var model = serializer.Deserialize<WorkflowDefinitionModel>(Source);
        var draft = await importer.ImportAsync(new() { Model = model, Publish = false });
        await Assert.That(draft.Succeeded).IsTrue();

        var definitions = host.Services.GetRequiredService<IWorkflowDefinitionService>();
        var loaded = await definitions.FindWorkflowGraphAsync("wrong-major", VersionOptions.Latest);
        var missing = (NotFoundActivity)loaded!.Nodes.Single(n => n.Activity.Id == "unresolved").Activity;
        await Assert.That(missing.MissingTypeName).IsEqualTo("Console.WriteLine");
        await Assert.That(missing.MissingTypeVersion).IsEqualTo(42);
        var publisher = host.Services.GetRequiredService<IWorkflowDefinitionPublisher>();
        var result = await publisher.PublishAsync("wrong-major");
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(host.CapturingTextWriter.Lines).IsEmpty();
    }
}
