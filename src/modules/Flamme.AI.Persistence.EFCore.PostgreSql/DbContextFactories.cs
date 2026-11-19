using Flamme.Persistence.EFCore.Abstractions;
using Flamme.Persistence.EFCore.Extensions;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Flamme.AI.Persistence.EFCore.PostgreSql;

[UsedImplicitly]
public class AIDbContextFactory : PostgreSqlAIDesignTimeDbContextFactory<AIDbContext>;

public class PostgreSqlAIDesignTimeDbContextFactory<TDbContext> : DesignTimeDbContextFactoryBase<TDbContext> where TDbContext : DbContext
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
