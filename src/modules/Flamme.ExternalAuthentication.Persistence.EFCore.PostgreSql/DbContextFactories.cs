using Flamme.ExternalAuthentication.Persistence.EFCore;
using Flamme.Persistence.EFCore.Abstractions;
using Flamme.Persistence.EFCore.Extensions;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Flamme.ExternalAuthentication.Persistence.EFCore.PostgreSql;

[UsedImplicitly]
public class ExternalAuthenticationDbContextFactory : PostgreSqlExternalAuthenticationDesignTimeDbContextFactory<ExternalAuthenticationFlammeDbContext>;

public class PostgreSqlExternalAuthenticationDesignTimeDbContextFactory<TDbContext> : DesignTimeDbContextFactoryBase<TDbContext> where TDbContext : DbContext
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
