using System.Net;
using Flamme.Api.Client.Resources.WorkflowDefinitions.Contracts;
using Flamme.Common.Models;
using Flamme.Common.Multitenancy;
using Flamme.Workflows.ComponentTests.Abstractions;
using Flamme.Workflows.ComponentTests.Fixtures;
using Flamme.Workflows.Management;
using Flamme.Workflows.Management.Activities.WorkflowDefinitionActivity;
using Flamme.Workflows.Management.Exceptions;
using Flamme.Workflows.Management.Models;
using Flamme.Workflows.Models;
using Microsoft.Extensions.DependencyInjection;
using Refit;
using ClientWorkflowModel = Flamme.Api.Client.Resources.WorkflowDefinitions.Models.WorkflowDefinitionModel;

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
        var sharedResult = await publisher.PublishAsync(shared);
        await Assert.That(sharedResult.Succeeded).IsTrue();
        var registry = Scope.ServiceProvider.GetRequiredService<IActivityRegistry>();
        var sharedBinding = registry.Find(shared.Name, shared.Version);
        await Assert.That(sharedBinding?.CustomProperties["WorkflowDefinitionId"]).IsEqualTo(shared.DefinitionId);

        var local = await publisher.NewAsync();
        local.Name = shared.Name;
        local.Options.UsableAsActivity = true;
        var localResult = await publisher.PublishAsync(local);
        await Assert.That(localResult.Succeeded).IsTrue();

        var selected = registry.Find(local.Name, local.Version);
        await Assert.That(selected!.CustomProperties["WorkflowDefinitionId"]).IsEqualTo(local.DefinitionId);

        var serializer = Scope.ServiceProvider.GetRequiredService<IActivitySerializer>();
        var json = $$"""{ "type": "{{shared.Name}}", "version": {{shared.Version}}, "workflowDefinitionId": "{{shared.DefinitionId}}", "workflowDefinitionVersionId": "obsolete-revision" }""";
        var child = await Assert.That(serializer.Deserialize<IActivity>(json)).IsTypeOf<WorkflowDefinitionActivity>();
        await Assert.That(child!.WorkflowDefinitionVersionId).IsEqualTo(shared.Id);
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

        var client = WorkflowServer.CreateApiClient<IWorkflowDefinitionsApi>();
        var rejected = await Assert.ThrowsExactlyAsync<ValidationApiException>(() => client.RevertVersionAsync(first.DefinitionId, 1));
        await Assert.That(rejected!.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        var versions = await store.FindManyAsync(new() { DefinitionId = first.DefinitionId });
        var head = versions.Single();
        await Assert.That(head.Id).IsEqualTo(first.Id);
        await Assert.That(head.IsLatest).IsTrue();
        await Assert.That(head.IsPublished).IsTrue();
    }

    [Test]
    public async Task RevertingToAMissingVersionPreservesTheCurrentHead()
    {
        var publisher = Scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionPublisher>();
        var store = Scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionStore>();
        var original = await publisher.NewAsync();
        original.Name = $"Notice{Guid.NewGuid():N}";
        await publisher.PublishAsync(original);

        var client = WorkflowServer.CreateApiClient<IWorkflowDefinitionsApi>();
        var missing = await Assert.ThrowsExactlyAsync<ApiException>(() => client.RevertVersionAsync(original.DefinitionId, 99));
        await Assert.That(missing!.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.ThrowsExactlyAsync<WorkflowDefinitionValidationException>(() => publisher.RevertVersionAsync(original.DefinitionId, 99));
        await Assert.ThrowsExactlyAsync<WorkflowDefinitionValidationException>(() => publisher.RevertVersionAsync(original.DefinitionId, 0));
        var head = (await store.FindManyAsync(new() { DefinitionId = original.DefinitionId })).Single();
        await Assert.That(head.Id).IsEqualTo(original.Id);
        await Assert.That(head.IsLatest).IsTrue();
        await Assert.That(head.IsPublished).IsTrue();
    }

    [Test]
    public async Task ImportAndDesignerReportNativeNameConflictsAsBadRequests()
    {
        var client = WorkflowServer.CreateApiClient<IWorkflowDefinitionsApi>();
        var name = $"Notice{Guid.NewGuid():N}";
        var model = new ClientWorkflowModel
        {
            DefinitionId = name + "-first", Name = name, Options = new() { UsableAsActivity = true }
        };
        await client.ImportAsync(model);
        model.DefinitionId = name + "-second";

        ApiException? rejected = await Assert.ThrowsExactlyAsync<ValidationApiException>(() => client.ImportAsync(model));
        await Assert.That(rejected!.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(rejected.Content!).Contains(name);
        foreach (var publish in new[] { false, true })
        {
            ApiException? save = await Assert.ThrowsExactlyAsync<ValidationApiException>(() => client.SaveAsync(new() { Model = model, Publish = publish }));
            await Assert.That(save!.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
            await Assert.That(save.Content!).Contains(name);
        }
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

        foreach (var revision in new[] { null, Guid.NewGuid().ToString("N") })
        {
            var rejected = await Import(name + "-second", publishSecond, revision);
            await Assert.That(rejected.Succeeded).IsFalse();
            await Assert.That(rejected.ValidationErrors.Single().Message).Contains(name);
        }
        var saved = await store.FindAsync(new() { DefinitionId = name + "-first" });
        await Assert.That(saved!.Id).IsEqualTo(first.WorkflowDefinition.Id);
        await Assert.That(saved.IsPublished).IsEqualTo(publishFirst);
        await Assert.That(saved.IsLatest).IsTrue();
        await Assert.That(await store.FindAsync(new() { DefinitionId = name + "-second" })).IsNull();

        Task<ImportWorkflowResult> Import(string owner, bool publish, string? revision = null) => importer.ImportAsync(new()
        {
            Model = new() { Id = revision!, DefinitionId = owner, Name = name, Options = new() { UsableAsActivity = true } },
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
