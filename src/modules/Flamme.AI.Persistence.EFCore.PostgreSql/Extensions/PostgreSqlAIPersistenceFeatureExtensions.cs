using System.Reflection;
using Flamme.AI.Persistence.EFCore.Features;
using Flamme.Persistence.EFCore;
using Flamme.Persistence.EFCore.Extensions;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace Flamme.AI.Persistence.EFCore.PostgreSql.Extensions;

public static class PostgreSqlAIPersistenceFeatureExtensions
{
    private static Assembly Assembly => typeof(PostgreSqlAIPersistenceFeatureExtensions).Assembly;

    public static AIPersistenceFeature UsePostgreSql(
        this AIPersistenceFeature feature,
        string connectionString,
        FlammeDbContextOptions? options = null,
        Action<NpgsqlDbContextOptionsBuilder>? configure = null)
    {
        return feature.UsePostgreSql(_ => connectionString, options, configure);
    }

    public static AIPersistenceFeature UsePostgreSql(
        this AIPersistenceFeature feature,
        Func<IServiceProvider, string> connectionStringFunc,
        FlammeDbContextOptions? options = null,
        Action<NpgsqlDbContextOptionsBuilder>? configure = null)
    {
        return feature.UsePostgreSql(Assembly, connectionStringFunc, options, configure);
    }
}
