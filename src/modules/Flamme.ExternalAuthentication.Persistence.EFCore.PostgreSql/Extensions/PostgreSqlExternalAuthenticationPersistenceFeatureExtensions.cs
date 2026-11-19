using Flamme.ExternalAuthentication.Persistence.EFCore.Features;
using Flamme.Persistence.EFCore.Extensions;
using Flamme.Persistence.EFCore;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;
using System.Reflection;

namespace Flamme.ExternalAuthentication.Persistence.EFCore.PostgreSql.Extensions;

public static class PostgreSqlExternalAuthenticationPersistenceFeatureExtensions
{
    private static Assembly Assembly => typeof(PostgreSqlExternalAuthenticationPersistenceFeatureExtensions).Assembly;

    public static EFCoreExternalAuthenticationPersistenceFeature UsePostgreSql(
        this EFCoreExternalAuthenticationPersistenceFeature feature,
        string connectionString,
        FlammeDbContextOptions? options = null,
        Action<NpgsqlDbContextOptionsBuilder>? configure = null)
    {
        return feature.UsePostgreSql(_ => connectionString, options, configure);
    }

    public static EFCoreExternalAuthenticationPersistenceFeature UsePostgreSql(
        this EFCoreExternalAuthenticationPersistenceFeature feature,
        Func<IServiceProvider, string> connectionStringFunc,
        FlammeDbContextOptions? options = null,
        Action<NpgsqlDbContextOptionsBuilder>? configure = null)
    {
        return feature.UsePostgreSql(Assembly, connectionStringFunc, options, configure);
    }
}
