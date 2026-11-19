using System.Reflection;
using Flamme.Persistence.EFCore;
using Flamme.Persistence.EFCore.Extensions;
using Flamme.Secrets.Persistence.EFCore.Features;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace Flamme.Secrets.Persistence.EFCore.PostgreSql.Extensions;

public static class PostgreSqlSecretsPersistenceFeatureExtensions
{
    private static Assembly Assembly => typeof(PostgreSqlSecretsPersistenceFeatureExtensions).Assembly;

    public static EFCoreSecretsPersistenceFeature UsePostgreSql(
        this EFCoreSecretsPersistenceFeature feature,
        string connectionString,
        FlammeDbContextOptions? options = null,
        Action<NpgsqlDbContextOptionsBuilder>? configure = null)
    {
        return feature.UsePostgreSql(_ => connectionString, options, configure);
    }

    public static EFCoreSecretsPersistenceFeature UsePostgreSql(
        this EFCoreSecretsPersistenceFeature feature,
        Func<IServiceProvider, string> connectionStringFunc,
        FlammeDbContextOptions? options = null,
        Action<NpgsqlDbContextOptionsBuilder>? configure = null)
    {
        return feature.UsePostgreSql(Assembly, connectionStringFunc, options, configure);
    }
}
