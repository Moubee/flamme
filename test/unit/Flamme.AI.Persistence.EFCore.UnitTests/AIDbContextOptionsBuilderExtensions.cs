using Elsa.AI.Persistence.EFCore.PostgreSql;
using Elsa.Persistence.EFCore;
using Elsa.Testing.Shared.Databases;
using Microsoft.EntityFrameworkCore;

namespace Elsa.AI.Persistence.EFCore.UnitTests;

internal static class AIDbContextOptionsBuilderExtensions
{
    public static DbContextOptionsBuilder<AIDbContext> UsePostgreSqlAIMigrations(this DbContextOptionsBuilder<AIDbContext> builder, PostgreSqlTestDatabase connection)
    {
        builder.UseElsaDbContextOptions(null);
        return builder.UseNpgsql(connection.Connection, db => db.MigrationsAssembly(typeof(AIDbContextFactory).Assembly.GetName().Name));
    }
}
