using Flamme.Extensions;
using Flamme.Persistence.EFCore.Extensions;
using Flamme.Persistence.EFCore.Modules.Management;
using Flamme.Scheduling.Activities;
using Flamme.Scheduling.Bookmarks;
using Flamme.Testing.Shared;
using Flamme.Testing.Shared.Databases;
using Flamme.Workflows.Activities;
using Flamme.Workflows.Management;
using Flamme.Workflows.Management.Filters;
using Flamme.Workflows.Management.Mappers;
using Flamme.Workflows.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Flamme.Workflows.IntegrationTests;

public class TimeProviderPersistenceTests
{
    // PostgreSQL timestamp columns preserve microseconds, while workflow state is stored as JSON.
    private static readonly DateTimeOffset StartedUtc = new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero).AddMicroseconds(123456);
    private static readonly DateTimeOffset ResumedUtc = StartedUtc.AddHours(2);
    private static readonly DateTimeOffset DueUtc = StartedUtc.AddHours(1);

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("AOT", "IL3050", Justification = "This JIT-run database test exercises EF schema creation or migrations; production AOT support is tracked separately.")]
    [Test]
    public async Task SuspendedWorkflow_PreservesPersistedInstantsAcrossProviderRebuildAndResumesWithNewTime()
    {
        await using var database = new PostgreSqlTestDatabase();
        await database.OpenAsync();
        string instanceId;
        string bookmarkId;

        await using (var firstHost = CreateHost(database.ConnectionString, new FakeTimeProvider(StartedUtc)))
        {
            await using (var dbContext = await firstHost.GetRequiredService<IDbContextFactory<ManagementFlammeDbContext>>().CreateDbContextAsync())
                await dbContext.Database.MigrateAsync();

            await firstHost.PopulateRegistriesAsync();
            var started = await firstHost.GetRequiredService<IWorkflowRunner>().RunAsync(CreateWorkflow());
            var bookmark = await Assert.That(started.WorkflowState.Bookmarks).HasSingleItem();
            instanceId = started.WorkflowState.Id;
            bookmarkId = bookmark.Id;

            await Assert.That(started.WorkflowState.SubStatus).IsEqualTo(WorkflowSubStatus.Suspended);
            await Assert.That(started.WorkflowState.CreatedAt).IsEqualTo(StartedUtc);
            await Assert.That(started.WorkflowState.UpdatedAt).IsEqualTo(StartedUtc);
            await Assert.That(bookmark.CreatedAt).IsEqualTo(StartedUtc);
            await Assert.That(bookmark.GetPayload<DelayPayload>().ResumeAt).IsEqualTo(DueUtc);

            var instance = firstHost.GetRequiredService<WorkflowStateMapper>().Map(started.WorkflowState)!;
            await firstHost.GetRequiredService<IWorkflowInstanceStore>().SaveAsync(instance);
        }

        await AssertResumesFromDatabaseAsync(database.ConnectionString, instanceId, bookmarkId);
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "This test exercises the production reflection serializer contract; replacing it would bypass the behavior under test.")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("AOT", "IL3050", Justification = "This JIT-run database test exercises EF schema creation or migrations; production AOT support is tracked separately.")]
    [Test]
    public async Task PreMigrationState_LoadsFromPostgreSqlAndResumesWithTimeProvider()
    {
        await using var database = new PostgreSqlTestDatabase();
        await database.OpenAsync();
        // Produced by IWorkflowRunner and IWorkflowStateSerializer at baseline commit 239ae5b99,
        // with the same workflow graph and a fixed legacy clock returning StartedUtc.
        // Preserve these original bytes: regenerating with the migrated code loses upgrade coverage.
        var legacyJson = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Serialization", "timeprovider-legacy-state.json"));
        string instanceId;
        string bookmarkId;

        await using (var importingHost = CreateHost(database.ConnectionString, new FakeTimeProvider(ResumedUtc)))
        {
            await using (var dbContext = await importingHost.GetRequiredService<IDbContextFactory<ManagementFlammeDbContext>>().CreateDbContextAsync())
                await dbContext.Database.MigrateAsync();

            await importingHost.PopulateRegistriesAsync();
            var legacyState = importingHost.GetRequiredService<IWorkflowStateSerializer>().Deserialize(legacyJson);
            instanceId = legacyState.Id;
            bookmarkId = legacyState.Bookmarks.Single().Id;
            var instance = importingHost.GetRequiredService<WorkflowStateMapper>().Map(legacyState)!;
            await importingHost.GetRequiredService<IWorkflowInstanceStore>().SaveAsync(instance);

            // Keep the baseline serializer's actual payload in the row, rather than a new serializer's
            // re-emission. The next host must deserialize those legacy bytes through the EF store.
            await using var persistedContext = await importingHost.GetRequiredService<IDbContextFactory<ManagementFlammeDbContext>>().CreateDbContextAsync();
            var persistedInstance = await persistedContext.WorkflowInstances.SingleAsync(x => x.Id == instanceId);
            persistedContext.Entry(persistedInstance).Property("Data").CurrentValue = legacyJson;
            persistedContext.Entry(persistedInstance).Property("DataCompressionAlgorithm").CurrentValue = "None";
            await persistedContext.SaveChangesAsync();
        }

        await AssertResumesFromDatabaseAsync(database.ConnectionString, instanceId, bookmarkId);
    }

    private static async Task AssertResumesFromDatabaseAsync(string connectionString, string instanceId, string bookmarkId)
    {

        // A completely new container and provider must load the database representation, not reuse
        // the first execution's object graph or services. Fake time does not drive a scheduler here;
        // the runner explicitly resumes the persisted bookmark after its stored due instant.
        var resumedTime = new FakeTimeProvider(ResumedUtc);
        await using var secondHost = CreateHost(connectionString, resumedTime);
        await secondHost.PopulateRegistriesAsync();
        var store = secondHost.GetRequiredService<IWorkflowInstanceStore>();
        var persisted = await Assert.That(await store.FindAsync(new WorkflowInstanceFilter { Id = instanceId })).IsNotNull();
        var persistedBookmark = await Assert.That(persisted.WorkflowState.Bookmarks).HasSingleItem();

        await Assert.That(secondHost.GetRequiredService<TimeProvider>()).IsSameReferenceAs(resumedTime);
        await Assert.That(persisted.CreatedAt).IsEqualTo(StartedUtc);
        await Assert.That(persisted.UpdatedAt).IsEqualTo(StartedUtc);
        await Assert.That(persisted.WorkflowState.CreatedAt).IsEqualTo(StartedUtc);
        await Assert.That(persisted.WorkflowState.UpdatedAt).IsEqualTo(StartedUtc);
        await Assert.That(persistedBookmark.Id).IsEqualTo(bookmarkId);
        await Assert.That(persistedBookmark.CreatedAt).IsEqualTo(StartedUtc);
        await Assert.That(persistedBookmark.GetPayload<DelayPayload>().ResumeAt).IsEqualTo(DueUtc);

        var resumed = await secondHost.GetRequiredService<IWorkflowRunner>().RunAsync(
            CreateWorkflow(), persisted.WorkflowState, new RunWorkflowOptions { BookmarkId = bookmarkId });

        await Assert.That(resumed.WorkflowState.Status).IsEqualTo(WorkflowStatus.Finished);
        await Assert.That(resumed.WorkflowState.CreatedAt).IsEqualTo(StartedUtc);
        await Assert.That(resumed.WorkflowState.UpdatedAt).IsEqualTo(ResumedUtc);
        await Assert.That(resumed.WorkflowState.FinishedAt).IsEqualTo(ResumedUtc);
        await Assert.That(resumed.WorkflowState.Bookmarks).IsEmpty();
        var afterResume = resumed.Journal.ActivityExecutionContexts.Single(x => x.Activity.Id == "after-resume");
        await Assert.That(afterResume.StartedAt).IsEqualTo(ResumedUtc);
        await Assert.That(afterResume.CompletedAt).IsEqualTo(ResumedUtc);

        await store.SaveAsync(secondHost.GetRequiredService<WorkflowStateMapper>().Map(resumed.WorkflowState)!);
        var completed = await Assert.That(await store.FindAsync(new WorkflowInstanceFilter { Id = instanceId })).IsNotNull();
        await Assert.That(completed.CreatedAt).IsEqualTo(StartedUtc);
        await Assert.That(completed.FinishedAt).IsEqualTo(ResumedUtc);
        await Assert.That(completed.WorkflowState.FinishedAt).IsEqualTo(ResumedUtc);
    }

    private static ServiceProvider CreateHost(string connectionString, TimeProvider timeProvider) =>
        (ServiceProvider)new TestApplicationBuilder(TestContext.Current!.Output.StandardOutput)
            .ConfigureServices(services => services.AddSingleton<TimeProvider>(timeProvider))
            .ConfigureFlamme(flamme => flamme.UseWorkflowManagement(management =>
                management.UseWorkflowInstances(instances => instances.UseEntityFrameworkCore(ef => ef.UsePostgreSql(connectionString)))))
            .Build();

    private static Workflow CreateWorkflow() => new()
    {
        Id = "timeprovider-workflow",
        Identity = new("timeprovider-persistence", 1, "timeprovider-persistence-v1"),
        Root = new Sequence
        {
            Id = "sequence",
            Activities =
            {
                new Delay(TimeSpan.FromHours(1)) { Id = "wait" },
                new WriteLine("resumed") { Id = "after-resume" }
            }
        }
    };
}
