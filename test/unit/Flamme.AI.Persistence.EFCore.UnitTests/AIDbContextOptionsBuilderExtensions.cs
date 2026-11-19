using Flamme.AI.Persistence.EFCore.PostgreSql;
using Flamme.Persistence.EFCore;
using Flamme.Testing.Shared.Databases;
using Microsoft.EntityFrameworkCore;

namespace Flamme.AI.Persistence.EFCore.UnitTests;

internal static class AIDbContextOptionsBuilderExtensions
{
    public static DbContextOptionsBuilder<AIDbContext> UsePostgreSqlAIMigrations(this DbContextOptionsBuilder<AIDbContext> builder, PostgreSqlTestDatabase connection)
    {
        builder.UseFlammeDbContextOptions(null);
        return builder.UseNpgsql(connection.Connection, db => db.MigrationsAssembly(typeof(AIDbContextFactory).Assembly.GetName().Name));
    }
}
