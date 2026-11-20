using System.Diagnostics.CodeAnalysis;
using Flamme.Extensions;
using Flamme.Persistence.EFCore;
using Flamme.Persistence.EFCore.Extensions;
using Flamme.Persistence.EFCore.Modules.Management;
using Flamme.Persistence.EFCore.PostgreSql;
using Flamme.Tenants.Extensions;
using Flamme.Testing.Shared;
using Flamme.Testing.Shared.Databases;
using Flamme.Workflows.Management;
using Flamme.Workflows.Management.Mappers;
using Flamme.Workflows.State;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Flamme.Workflows.IntegrationTests;

public sealed class WorkflowInstanceHostConfigurationTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "This integration test migrates a real PostgreSQL database.")]
    public async Task WorkflowStoresHonorEachHostsTenantConfigurationInOneProcess(bool useOptionsCallback)
    {
        await using var database = new PostgreSqlTestDatabase();
        await database.OpenAsync();
        await using var plainHost = CreateHost(false);
        await using (var db = await plainHost.GetRequiredService<IDbContextFactory<ManagementFlammeDbContext>>().CreateDbContextAsync())
            await db.Database.MigrateAsync();

        var plain = plainHost.GetRequiredService<IWorkflowInstanceStore>();
        var mapper = plainHost.GetRequiredService<WorkflowStateMapper>();
        var imported = mapper.Map(new WorkflowState { Id = "imported", DefinitionId = "workflow", DefinitionVersionId = "v1" })!;
        imported.TenantId = "east";
        await plain.SaveAsync(imported);

        await using var tenantHost = CreateHost(true);
        var tenantAware = tenantHost.GetRequiredService<IWorkflowInstanceStore>();
        var current = mapper.Map(new WorkflowState { Id = "current", DefinitionId = "workflow", DefinitionVersionId = "v1" })!;
        await tenantAware.SaveAsync(current);

        await Assert.That(await tenantAware.FindAsync(new() { Id = current.Id })).IsNotNull();
        await Assert.That(await tenantAware.FindAsync(new() { Id = imported.Id })).IsNull();
        await Assert.That(await plain.FindAsync(new() { Id = imported.Id })).IsNotNull();

        ServiceProvider CreateHost(bool useTenants) =>
            (ServiceProvider)new TestApplicationBuilder(TestContext.Current!.Output.StandardOutput)
                .ConfigureFlamme(flamme =>
                {
                    if (useTenants)
                        flamme.UseTenants();
                    flamme.UseWorkflowManagement(management => management.UseWorkflowInstances(instances =>
                        instances.UseEntityFrameworkCore(ConfigurePersistence)));
                })
                .Build();

        void ConfigurePersistence(EFCoreWorkflowInstancePersistenceFeature feature)
        {
            if (!useOptionsCallback)
            {
                feature.UsePostgreSql(database.ConnectionString);
                return;
            }

            feature.Module.Services.AddPostgreSqlEntityModelCreatingHandlers();
            feature.DbContextOptionsBuilder = (_, options) => options
                .ReplaceService<IMigrationsAssembly, DbSchemaAwareMigrationAssembly>()
                .UseNpgsql(database.ConnectionString, provider => provider.MigrationsAssembly(typeof(ManagementDbContextFactory).Assembly)
                    .MigrationsHistoryTable(FlammeDbContextBase.MigrationsHistoryTable, FlammeDbContextBase.FlammeSchema));
        }
    }
}
