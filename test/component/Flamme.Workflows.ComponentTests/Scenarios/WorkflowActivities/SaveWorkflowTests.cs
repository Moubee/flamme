using Flamme.Common.Models;
using Flamme.Common.Multitenancy;
using Flamme.Workflows.ComponentTests.Abstractions;
using Flamme.Workflows.ComponentTests.Fixtures;
using Flamme.Workflows.Management;
using Flamme.Workflows.Management.Activities.WorkflowDefinitionActivity;
using Flamme.Workflows.Management.Models;
using Flamme.Workflows.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Flamme.Workflows.ComponentTests.Scenarios.WorkflowActivities;

public class SaveWorkflowTests(App app) : AppComponentTest(app)
{
    [Test]
    public async Task TenantWorkflowCanOverrideASharedNativeActivity()
    {
        var publisher = Scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionPublisher>();
        var shared = await publisher.NewAsync();
        shared.Name = $"Notice{Guid.NewGuid():N}";
        shared.TenantId = Tenant.AgnosticTenantId;
        shared.Options.UsableAsActivity = true;
        await publisher.PublishAsync(shared);

        var local = await publisher.NewAsync();
        local.Name = shared.Name;
        local.Options.UsableAsActivity = true;
        await publisher.PublishAsync(local);

        var registry = Scope.ServiceProvider.GetRequiredService<IActivityRegistry>();
        var selected = registry.Find(local.Name, local.Version);
        await Assert.That(selected!.CustomProperties["WorkflowDefinitionId"]).IsEqualTo(local.DefinitionId);
    }

    [Test]
    public async Task RevertingToAConflictingNativeActivityPreservesTheCurrentHead()
    {
        var publisher = Scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionPublisher>();
        var store = Scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionStore>();
        var first = await publisher.NewAsync();
        first.Name = $"Notice{Guid.NewGuid():N}";
        first.Options.UsableAsActivity = true;
        await publisher.PublishAsync(first);

        var second = await publisher.NewAsync();
        second.Name = first.Name + "Other";
        second.Options.UsableAsActivity = true;
        await publisher.PublishAsync(second);
        var secondDraft = await publisher.GetDraftAsync(second.DefinitionId, VersionOptions.Latest);
        secondDraft!.Name = first.Name;
        await publisher.PublishAsync(secondDraft);

        await Assert.That(async () => { await publisher.RevertVersionAsync(first.DefinitionId, 1); }).Throws<InvalidOperationException>();
        var versions = await store.FindManyAsync(new() { DefinitionId = first.DefinitionId });
        var head = versions.Single();
        await Assert.That(head.Id).IsEqualTo(first.Id);
        await Assert.That(head.IsLatest).IsTrue();
        await Assert.That(head.IsPublished).IsTrue();
    }

    [Test]
    [Arguments(true, true)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(false, false)]
    public async Task ConflictingNativeBindingIsRejectedBeforeSaving(bool publishFirst, bool publishSecond)
    {
        var importer = Scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionImporter>();
        var store = Scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionStore>();
        var name = $"Notice{Guid.NewGuid():N}";
        var first = await Import(name + "-first", publishFirst);
        await Assert.That(first.Succeeded).IsTrue();

        await Assert.That(async () => { await Import(name + "-second", publishSecond); }).Throws<InvalidOperationException>();
        var saved = await store.FindAsync(new() { DefinitionId = name + "-first" });
        await Assert.That(saved!.Id).IsEqualTo(first.WorkflowDefinition.Id);
        await Assert.That(saved.IsPublished).IsEqualTo(publishFirst);
        await Assert.That(saved.IsLatest).IsTrue();
        await Assert.That(await store.FindAsync(new() { DefinitionId = name + "-second" })).IsNull();

        Task<ImportWorkflowResult> Import(string owner, bool publish) => importer.ImportAsync(new()
        {
            Model = new() { DefinitionId = owner, Name = name, Options = new() { UsableAsActivity = true } },
            Publish = publish
        });
    }

    [Test]
    [DisplayName("Saving workflows updates ActivityRegistry (name: $name, usableAsActivity: $usableAsActivity, publish: $publish, expectedInRegistry: $expectedInRegistry, isBrowsable: $isBrowsable)")]
    [Arguments("Save1", true, true, true, true)]
    [Arguments("Save2", true, false, false, false)]
    [Arguments("Save3", false, true, false, false)]
    [Arguments("Save4", false, false, false, false)]
    public async Task ActivityRegistry(string name, bool usableAsActivity, bool publish, bool expectedInRegistry, bool isBrowsable)
    {
        var activityRegistry = Scope.ServiceProvider.GetRequiredService<IActivityRegistry>();

        var descriptor = activityRegistry.Find(name);
        if (descriptor is not null)
            activityRegistry.Remove(typeof(WorkflowDefinitionActivityProvider), descriptor);

        var importer = Scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionImporter>();
        var request = new SaveWorkflowDefinitionRequest
        {
            Model = new WorkflowDefinitionModel
            {
                Name = name,
                DefinitionId = name,
                Options = new WorkflowOptions
                {
                    UsableAsActivity = usableAsActivity,
                    AutoUpdateConsumingWorkflows = true
                }
            },
            Publish = publish
        };
        await importer.ImportAsync(request);

        descriptor = activityRegistry.Find(name);

        if (expectedInRegistry)
        {
            await Assert.That(descriptor).IsNotNull();
            await Assert.That(descriptor.IsBrowsable).IsEqualTo(isBrowsable);
        }
        else
        {
            await Assert.That(descriptor).IsNull();
        }
    }
}
