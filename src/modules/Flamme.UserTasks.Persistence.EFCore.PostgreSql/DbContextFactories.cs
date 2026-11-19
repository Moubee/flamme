using Flamme.Persistence.EFCore.Abstractions;
using Flamme.Persistence.EFCore.Extensions;
using Flamme.UserTasks.Persistence.EFCore;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Flamme.UserTasks.Persistence.EFCore.PostgreSql;

[UsedImplicitly]
public class UserTasksDbContextFactory : PostgreSqlUserTasksDesignTimeDbContextFactory<UserTasksFlammeDbContext>;
public class PostgreSqlUserTasksDesignTimeDbContextFactory<TDbContext> : DesignTimeDbContextFactoryBase<TDbContext> where TDbContext : DbContext
{
    protected override void ConfigureServices(IServiceCollection services) => services.AddPostgreSqlEntityModelCreatingHandlers();
    protected override void ConfigureBuilder(DbContextOptionsBuilder<TDbContext> builder, string connectionString) => builder.UseFlammePostgreSql(GetType().Assembly, connectionString);
}
