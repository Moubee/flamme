using Flamme.Common.Models;
using Flamme.Extensions;
using Flamme.Persistence.EFCore;
using Flamme.Workflows.Management;
using Flamme.Workflows.Management.Exceptions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Flamme.Workflows.Management.Entities;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Flamme.Workflows.Persistence.ConformanceTests;

public class WorkflowPublicationTests
{
    [Test]
    public async Task PublicationRunsEntitySavingHooksAndPreservesTheHeadOnFailure()
    {
        var hook = new PublicationHook();
        await using var scenario = await WorkflowStoreScenario.CreatePostgreSqlAsync("tenant-a", tenantsEnabled: false, savingHandler: hook);
        var store = scenario.Definitions;
        var v1 = Revision("revision-1", 1);
        await store.SavePublicationAsync([v1]);
        var previous = (await store.FindAsync(new() { Id = v1.Id }))!;
        await Assert.That(previous.TenantId).IsNull();
        previous.IsLatest = previous.IsPublished = false;

        var v2 = Revision("revision-2", 2);
        hook.Reject = v2.Id;
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => store.SavePublicationAsync([previous, v2]));
        await Assert.That((await store.FindAsync(new() { Id = v1.Id }))!.IsPublished).IsTrue();
        await Assert.That(await store.FindAsync(new() { Id = v2.Id })).IsNull();

        hook.Reject = null;
        hook.SavedIds.Clear();
        await store.SavePublicationAsync([previous, v2]);
        await Assert.That(hook.SavedIds).IsEquivalentTo([v1.Id, v2.Id]);
        await Assert.That((await store.FindAsync(new() { Id = v1.Id }))!.IsPublished).IsFalse();
        await Assert.That((await store.FindAsync(new() { Id = v2.Id }))!.IsPublished).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CompetingPublicationsFromOneSnapshotCannotCreateTwoHeads(bool postgres)
    {
        await using var scenario = postgres ? await WorkflowStoreScenario.CreatePostgreSqlAsync() : await WorkflowStoreScenario.CreateInMemoryAsync();
        var store = scenario.Definitions;
        var v1 = Revision("v1", 1);
        v1.TenantId = "tenant-a";
        await store.SavePublicationAsync([v1]);

        // Both writers observed v1. They carry distinct new version keys, so only a
        // predecessor check can detect the stale publication once either one commits.
        var demoted = v1.ShallowClone();
        demoted.IsLatest = demoted.IsPublished = false;
        var attempts = await Task.WhenAll(PublishAsync("v2", 2), PublishAsync("v3", 3));
        await Assert.That(attempts.Count(accepted => accepted)).IsEqualTo(1);
        var persisted = (await store.FindManyAsync(new() { DefinitionId = v1.DefinitionId })).ToList();
        await Assert.That(persisted.Count).IsEqualTo(2);
        var head = await Assert.That(persisted.Where(x => x.IsLatest || x.IsPublished)).HasSingleItem();
        await Assert.That(head!.Id).IsEqualTo(attempts[0] ? "v2" : "v3");
        await Assert.That(head.IsLatest && head.IsPublished).IsTrue();

        async Task<bool> PublishAsync(string id, int version)
        {
            var candidate = Revision(id, version);
            candidate.TenantId = v1.TenantId;
            try
            {
                await store.SavePublicationAsync([demoted, candidate]);
                return true;
            }
            catch (WorkflowDefinitionValidationException)
            {
                return false;
            }
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SavingAStaleDraftCannotUndoPublication(bool postgres)
    {
        await using var scenario = postgres ? await WorkflowStoreScenario.CreatePostgreSqlAsync() : await WorkflowStoreScenario.CreateInMemoryAsync();
        var store = scenario.Definitions;
        var v1 = Revision("v1", 1);
        v1.TenantId = "tenant-a";
        v1.IsPublished = false;
        await store.SaveDraftVersionAsync(null, v1);
        var expected = v1.ShallowClone();
        var draft = v1.ShallowClone();
        var published = v1.ShallowClone();
        published.IsPublished = true;
        await store.SavePublicationAsync([published]);

        // Another request published the same draft after our read.
        await Assert.ThrowsExactlyAsync<WorkflowDefinitionValidationException>(() => store.SaveDraftVersionAsync(expected, draft));
        await Assert.That((await store.FindAsync(new() { Id = v1.Id }))!.IsPublished).IsTrue();
        await Assert.ThrowsExactlyAsync<WorkflowDefinitionValidationException>(() => store.SaveDraftVersionAsync(published, draft));

        var v2 = Revision("v2", 2);
        v2.TenantId = v1.TenantId;
        var previous = published.ShallowClone();
        previous.IsLatest = previous.IsPublished = false;
        await store.SavePublicationAsync([previous, v2]);
        // A new head after our read must not allow the old draft to become latest again.
        await Assert.ThrowsExactlyAsync<WorkflowDefinitionValidationException>(() => store.SaveDraftVersionAsync(expected, draft));
        var heads = await store.FindManyAsync(new() { DefinitionId = v1.DefinitionId, VersionOptions = VersionOptions.LatestOrPublished });
        await Assert.That((await Assert.That(heads).HasSingleItem()).Id).IsEqualTo(v2.Id);
    }

    [Test]
    public async Task RevertingRunsSavingHooksOnBothRevisionsAndRollsBackTogether()
    {
        var hook = new PublicationHook();
        await using var scenario = await WorkflowStoreScenario.CreatePostgreSqlAsync("tenant-a", tenantsEnabled: false, savingHandler: hook);
        var store = scenario.Definitions;
        var services = new ServiceCollection();
        services.AddLogging().AddSingleton<IConfiguration>(new ConfigurationManager());
        services.AddFlamme(flamme => flamme.UseWorkflowManagement());
        services.AddSingleton(store);
        await using var host = services.BuildServiceProvider();
        var publisher = host.GetRequiredService<IWorkflowDefinitionPublisher>();
        var v1 = Revision("v1", 1);
        await store.SavePublicationAsync([v1]);
        hook.Reject = v1.Id;
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => publisher.RevertVersionAsync(v1.DefinitionId, 1));
        var rows = await store.FindManyAsync(new() { DefinitionId = v1.DefinitionId });
        var unchanged = await Assert.That(rows).HasSingleItem();
        await Assert.That(unchanged!.IsLatest && unchanged.IsPublished).IsTrue();

        hook.Reject = null;
        hook.SavedIds.Clear();
        var draft = await publisher.RevertVersionAsync(v1.DefinitionId, 1);
        await Assert.That(hook.SavedIds).IsEquivalentTo([v1.Id, draft.Id]);
        var published = await store.FindAsync(new() { DefinitionId = v1.DefinitionId, VersionOptions = VersionOptions.Published });
        await Assert.That(published!.Id).IsEqualTo(v1.Id);
        await Assert.That(published.IsLatest).IsFalse();
        var latest = await store.FindAsync(new() { DefinitionId = v1.DefinitionId, VersionOptions = VersionOptions.Latest });
        await Assert.That(latest!.Id).IsEqualTo(draft.Id);
        await Assert.That(latest.IsPublished).IsFalse();
    }

    private static WorkflowDefinition Revision(string id, int version) => new()
    {
        Id = id, DefinitionId = "notices", Version = version, IsLatest = true, IsPublished = true,
        CreatedAt = DateTimeOffset.UtcNow, MaterializerName = "Json"
    };

    [Test]
    public async Task ConcurrentFirstImportsCanClaimARevisionIdOnlyOnce()
    {
        await using var scenario = await WorkflowStoreScenario.CreatePostgreSqlAsync("tenant-a", tenantsEnabled: false);
        var north = Revision("shared-revision", 1);
        var south = Revision("shared-revision", 1);
        south.DefinitionId = "other";
        var accepted = await Task.WhenAll(scenario.Definitions.TryAddAsync(north), scenario.Definitions.TryAddAsync(south));
        await Assert.That(accepted.Count(x => x)).IsEqualTo(1);
        var saved = await scenario.Definitions.FindAsync(new() { Id = north.Id });
        await Assert.That(saved!.DefinitionId).IsEqualTo(accepted[0] ? north.DefinitionId : south.DefinitionId);
    }

    private sealed class PublicationHook : IEntitySavingHandler
    {
        public string? Reject { get; set; }
        public List<string> SavedIds { get; } = [];

        public ValueTask HandleAsync(FlammeDbContextBase dbContext, EntityEntry entry, CancellationToken cancellationToken)
        {
            if (entry.Entity is WorkflowDefinition definition)
            {
                if (definition.Id == Reject)
                    throw new InvalidOperationException("Publication rejected by host.");
                SavedIds.Add(definition.Id);
            }
            return ValueTask.CompletedTask;
        }
    }
}
