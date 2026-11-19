using Flamme.Common.Models;
using Flamme.Common.Multitenancy;
using Flamme.Extensions;
using Flamme.Testing.Shared;
using Flamme.Workflows.Management;
using Flamme.Workflows.Models;
using Flamme.Workflows.Runtime;
using Flamme.Workflows.Runtime.Authoring;
using Flamme.Workflows.Runtime.Messages;
using Microsoft.Extensions.DependencyInjection;

namespace Flamme.Workflows.IntegrationTests.Scenarios.WorkflowDefinitionVersioning;

public class CuratedTenantTests
{
    [Test]
    public async Task NamedTenantCanPublishAndRunSuccessiveRevisions()
    {
        var services = new TestApplicationBuilder(TestContext.Current!.Output.StandardOutput).Build();
        try
        {
            await services.PopulateRegistriesAsync();
            var tenants = services.GetRequiredService<ITenantAccessor>();
            var publisher = services.GetRequiredService<CuratedWorkflowPublisher>();
            var store = services.GetRequiredService<IWorkflowDefinitionStore>();
            var catalog = new ProducerCatalog([]);
            var source = new AuthoredWorkflow("tenant-mail", [], []);

            using (tenants.PushContext(new Tenant { Id = "north" }))
            {
                var first = await publisher.PublishAsync(catalog, source);
                var second = await publisher.PublishAsync(catalog, source);
                await Assert.That(first.TenantId).IsEqualTo("north");
                await Assert.That(second.Version).IsEqualTo(2);
                await Assert.That((await store.FindAsync(new() { Id = first.Id }))!.IsPublished).IsFalse();
                var current = await store.FindAsync(new() { DefinitionId = source.DefinitionId, VersionOptions = VersionOptions.Published });
                await Assert.That(current!.Id).IsEqualTo(second.Id);
                var client = await services.GetRequiredService<IWorkflowRuntime>().CreateClientAsync();
                var run = await client.CreateAndRunInstanceAsync(new CreateAndRunWorkflowInstanceRequest
                {
                    WorkflowDefinitionHandle = WorkflowDefinitionHandle.ByDefinitionId(source.DefinitionId, VersionOptions.Published)
                });
                await Assert.That(run.SubStatus).IsEqualTo(WorkflowSubStatus.Finished);
            }

            using (tenants.PushContext(new Tenant { Id = "south" }))
                await Assert.That(await store.FindLastVersionAsync(new() { DefinitionId = source.DefinitionId }, CancellationToken.None)).IsNull();
        }
        finally
        {
            await TestResourceDisposal.DisposeAsync(services);
        }
    }
}
