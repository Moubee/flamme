using Flamme.Persistence.EFCore;
using Flamme.Workflows.Management.Entities;
using Flamme.Workflows.Management.Exceptions;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Flamme.Workflows.Persistence.ConformanceTests;

public class WorkflowActivityBindingTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AnImportAndAPublicationCannotClaimTheSameNativeBinding(bool postgreSql)
    {
        await using var scenario = postgreSql ? await WorkflowStoreScenario.CreatePostgreSqlAsync() : await WorkflowStoreScenario.CreateInMemoryAsync();
        var store = scenario.Definitions;
        var imported = Definition("first", "Ship Order");
        var published = Definition("second", "ShipOrder");
        published.IsPublished = true;
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<bool> ClaimAsync(Func<Task> write)
        {
            await start.Task;
            try
            {
                await write();
                return true;
            }
            catch (WorkflowDefinitionValidationException)
            {
                return false;
            }
        }

        var first = ClaimAsync(async () => await Assert.That(await store.TryAddAsync(imported)).IsTrue());
        var second = ClaimAsync(() => store.SavePublicationAsync([published]));
        start.SetResult();
        var outcomes = await Task.WhenAll(first, second);

        await Assert.That(outcomes.Count(won => won)).IsEqualTo(1);
        var rows = (await store.FindManyAsync(new() { UsableAsActivity = true })).ToList();
        var winner = await Assert.That(rows).HasSingleItem();
        await Assert.That(winner.Id).IsEqualTo(outcomes[0] ? imported.Id : published.Id);

        // Preserve the old owner's claim if a failed batch also rewrites another draft.
        var unrelated = Definition("unrelated", "Original");
        await store.SaveAsync(unrelated);
        var edited = Definition("unrelated", "Edited");
        var contender = Definition("contender", "Ship Order");
        await Assert.ThrowsExactlyAsync<WorkflowDefinitionValidationException>(() => store.SaveManyAsync([edited, contender]));
        await Assert.That((await store.FindAsync(new() { Id = unrelated.Id }))!.Name).IsEqualTo("Original");
        await Assert.That(await store.FindAsync(new() { Id = contender.Id })).IsNull();

        // A rename releases its old binding, and other tenants may use the same descriptor.
        var renamed = Definition(winner.DefinitionId, "Renamed");
        await store.SaveAsync(renamed);
        await store.SaveAsync(contender);
        using (scenario.UseTenant("tenant-b"))
        {
            var overrideDefinition = Definition("override", "ShipOrder");
            overrideDefinition.TenantId = "tenant-b";
            await store.SaveAsync(overrideDefinition);
            await Assert.That((await store.FindAsync(new() { Id = overrideDefinition.Id }))!.Name).IsEqualTo("ShipOrder");
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RejectedCompareAndSwapLeavesTheDraftUnbound(bool postgreSql)
    {
        await using var scenario = postgreSql ? await WorkflowStoreScenario.CreatePostgreSqlAsync() : await WorkflowStoreScenario.CreateInMemoryAsync();
        var store = scenario.Definitions;
        await store.SaveAsync(Definition("owner", "Ship Order"));
        var draft = Definition("draft", "ShipOrder");
        draft.Options.UsableAsActivity = false;
        await store.SaveAsync(draft);

        await Assert.ThrowsExactlyAsync<WorkflowDefinitionValidationException>(() => store.TryUpdateLatestAsync(
            new() { Id = draft.Id }, _ => true, current =>
            {
                // The EF compare-and-swap updates the payload and flag, leaving the persisted name intact.
                if (postgreSql)
                    current.Name = "Ignored name";
                current.Options.UsableAsActivity = true;
                return current;
            }));

        var stored = await store.FindAsync(new() { Id = draft.Id });
        await Assert.That(stored!.Name).IsEqualTo("ShipOrder");
        await Assert.That(stored.Options.UsableAsActivity).IsEqualTo(false);
    }

    [Test]
    public async Task SavingHooksCannotRenameADemotedActivityIntoAnotherOwnersBinding()
    {
        await using var scenario = await WorkflowStoreScenario.CreatePostgreSqlAsync("tenant-a", savingHandler: new RenamePredecessor());
        var store = scenario.Definitions;
        var previous = Definition("original", "Original");
        previous.IsPublished = true;
        await store.SavePublicationAsync([previous]);
        await store.SaveAsync(Definition("other", "Ship Order"));

        var revision = Definition("original", "New");
        revision.Id = "original-v2";
        revision.Version = 2;
        revision.IsPublished = true;
        await Assert.ThrowsExactlyAsync<WorkflowDefinitionValidationException>(() => store.SavePublicationAsync([previous, revision]));

        var retained = await store.FindAsync(new() { Id = previous.Id });
        await Assert.That(retained!.Name).IsEqualTo("Original");
        await Assert.That(retained.IsPublished).IsTrue();
        await Assert.That(await store.FindAsync(new() { Id = revision.Id })).IsNull();
    }

    private sealed class RenamePredecessor : IEntitySavingHandler
    {
        public ValueTask HandleAsync(FlammeDbContextBase dbContext, EntityEntry entry, CancellationToken cancellationToken)
        {
            if (entry.Entity is WorkflowDefinition { Id: "original-v1", IsLatest: false } predecessor)
                predecessor.Name = "ShipOrder";
            return ValueTask.CompletedTask;
        }
    }

    private static WorkflowDefinition Definition(string owner, string name) => new()
    {
        Id = owner + "-v1", DefinitionId = owner, Name = name, Version = 1, TenantId = "tenant-a", IsLatest = true,
        Options = new() { UsableAsActivity = true }, CreatedAt = DateTimeOffset.UtcNow, MaterializerName = "Json"
    };
}
