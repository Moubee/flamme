using System.Diagnostics.CodeAnalysis;
using Flamme.Extensions;
using Flamme.Persistence.EFCore.Extensions;
using Flamme.Persistence.EFCore.Modules.Management;
using Flamme.Tenants.Extensions;
using Flamme.Testing.Shared;
using Flamme.Testing.Shared.Databases;
using Flamme.Workflows.Management;
using Flamme.Workflows.Management.Mappers;
using Flamme.Workflows.State;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Flamme.Workflows.IntegrationTests;

public sealed class PostgreSqlWorkflowHeartbeatTests
{
    [Test]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "This integration test migrates a real PostgreSQL database.")]
    public async Task DelayedHeartbeatKeepsTheNewestStoredTimestamp()
    {
        await using var database = new PostgreSqlTestDatabase();
        await database.OpenAsync();
        await using var services = (ServiceProvider)new TestApplicationBuilder(TestContext.Current!.Output.StandardOutput)
            .ConfigureFlamme(flamme => flamme.UseTenants().UseWorkflowManagement(management =>
                management.UseWorkflowInstances(instances => instances.UseEntityFrameworkCore(ef => ef.UsePostgreSql(database.ConnectionString)))))
            .Build();
        await using var scope = services.CreateAsyncScope();
        var contexts = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ManagementFlammeDbContext>>();
        await using (var db = await contexts.CreateDbContextAsync())
            await db.Database.MigrateAsync();

        var store = scope.ServiceProvider.GetRequiredService<IWorkflowInstanceStore>();
        var initial = DateTimeOffset.UnixEpoch;
        var instance = scope.ServiceProvider.GetRequiredService<WorkflowStateMapper>().Map(new WorkflowState
        {
            Id = "heartbeat", DefinitionId = "workflow", DefinitionVersionId = "v1", UpdatedAt = initial
        })!;
        await store.AddAsync(instance);

        var latest = initial.AddMinutes(2);
        await store.UpdateUpdatedTimestampAsync(instance.Id, latest);
        await store.UpdateUpdatedTimestampAsync(instance.Id, latest);
        await store.UpdateUpdatedTimestampAsync(instance.Id, initial.AddMinutes(1));

        var stored = await Assert.That(await store.FindAsync(new() { Id = instance.Id })).IsNotNull();
        await Assert.That(stored.UpdatedAt).IsEqualTo(latest);
    }
}
