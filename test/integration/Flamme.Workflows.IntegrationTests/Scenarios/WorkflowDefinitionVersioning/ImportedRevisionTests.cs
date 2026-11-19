using Flamme.Extensions;
using Flamme.Testing.Shared;
using Flamme.Workflows.Activities;
using Flamme.Workflows.Management;
using Flamme.Workflows.Management.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Flamme.Workflows.IntegrationTests.Scenarios.WorkflowDefinitionVersioning;

public class ImportedRevisionTests
{
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task ImportPreservesNewIdsAndRejectsRevisionIdCollisions(bool publish)
    {
        var services = new TestApplicationBuilder(TestContext.Current!.Output.StandardOutput).Build();
        try
        {
            await services.PopulateRegistriesAsync();
            var importer = services.GetRequiredService<IWorkflowDefinitionImporter>();
            var store = services.GetRequiredService<IWorkflowDefinitionStore>();
            var first = await importer.ImportAsync(new SaveWorkflowDefinitionRequest
            {
                Publish = true,
                Model = new() { DefinitionId = "original", Id = "pinned", IsReadonly = true, Root = new WriteLine("v1") }
            });
            await Assert.That(first.Succeeded).IsTrue();
            await Assert.That(first.WorkflowDefinition.Id).IsEqualTo("pinned");
            var originalGraph = first.WorkflowDefinition.StringData;

            var duplicate = await importer.ImportAsync(new SaveWorkflowDefinitionRequest
            {
                Publish = publish,
                Model = new() { DefinitionId = "copy", Id = "pinned", Root = new WriteLine("different") }
            });
            await Assert.That(duplicate.Succeeded).IsFalse();
            await Assert.That(duplicate.ValidationErrors).HasSingleItem();
            var pinned = await store.FindAsync(new() { Id = "pinned" });
            await Assert.That(pinned!.DefinitionId).IsEqualTo("original");
            await Assert.That(pinned.StringData).IsEqualTo(originalGraph);
            await Assert.That(pinned.IsPublished).IsTrue();
            await Assert.That(await store.FindAsync(new() { DefinitionId = "copy" })).IsNull();
        }
        finally
        {
            await TestResourceDisposal.DisposeAsync(services);
        }
    }
}
