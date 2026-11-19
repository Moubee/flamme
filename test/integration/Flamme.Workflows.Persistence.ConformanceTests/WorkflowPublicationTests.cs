using Flamme.Persistence.EFCore;
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

    private static WorkflowDefinition Revision(string id, int version) => new()
    {
        Id = id, DefinitionId = "notices", Version = version, IsLatest = true, IsPublished = true,
        CreatedAt = DateTimeOffset.UtcNow, MaterializerName = "Json"
    };

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
