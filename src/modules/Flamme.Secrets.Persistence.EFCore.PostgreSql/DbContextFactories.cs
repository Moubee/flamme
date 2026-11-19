using Flamme.Persistence.EFCore.Abstractions;
using Flamme.Persistence.EFCore.Extensions;
using Flamme.Secrets.Persistence.EFCore;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Flamme.Secrets.Persistence.EFCore.PostgreSql;

[UsedImplicitly]
public class SecretsDbContextFactory : PostgreSqlSecretsDesignTimeDbContextFactory<SecretsFlammeDbContext>;

public class PostgreSqlSecretsDesignTimeDbContextFactory<TDbContext> : DesignTimeDbContextFactoryBase<TDbContext> where TDbContext : DbContext
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddPostgreSqlEntityModelCreatingHandlers();
    }

    protected override void ConfigureBuilder(DbContextOptionsBuilder<TDbContext> builder, string connectionString)
    {
        builder.UseFlammePostgreSql(GetType().Assembly, connectionString);
    }
}
