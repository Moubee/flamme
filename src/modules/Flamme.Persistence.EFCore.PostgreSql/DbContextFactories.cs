using Flamme.Persistence.EFCore.Abstractions;
using Flamme.Persistence.EFCore.Extensions;
using Flamme.Persistence.EFCore.Modules.Alterations;
using Flamme.Persistence.EFCore.Modules.Identity;
using Flamme.Persistence.EFCore.Modules.Labels;
using Flamme.Persistence.EFCore.Modules.Management;
using Flamme.Persistence.EFCore.Modules.Runtime;
using Flamme.Persistence.EFCore.Modules.Tenants;
using Flamme.Persistence.EFCore.PostgreSql.Handlers;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

namespace Flamme.Persistence.EFCore.PostgreSql;

[UsedImplicitly]
public class IdentityDbContextFactory : PostgreSqlDesignTimeDbContextFactory<IdentityFlammeDbContext>;

[UsedImplicitly]
public class ManagementDbContextFactory : PostgreSqlDesignTimeDbContextFactory<ManagementFlammeDbContext>;

[UsedImplicitly]
public class RuntimeDbContextFactory : PostgreSqlDesignTimeDbContextFactory<RuntimeFlammeDbContext>;

[UsedImplicitly]
public class LabelsDbContextFactory : PostgreSqlDesignTimeDbContextFactory<LabelsFlammeDbContext>;

[UsedImplicitly]
public class AlterationsDbContextFactories : PostgreSqlDesignTimeDbContextFactory<AlterationsFlammeDbContext>;

[UsedImplicitly]
public class TenantsDbContextFactories : PostgreSqlDesignTimeDbContextFactory<TenantsFlammeDbContext>;

public class PostgreSqlDesignTimeDbContextFactory<TDbContext> : DesignTimeDbContextFactoryBase<TDbContext> where TDbContext : DbContext
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IEntityModelCreatingHandler, SetupForPostgreSql>();
    }
    
    protected override void ConfigureBuilder(DbContextOptionsBuilder<TDbContext> builder, string connectionString)
    {
        builder.UseFlammePostgreSql(GetType().Assembly, connectionString);
    }
}