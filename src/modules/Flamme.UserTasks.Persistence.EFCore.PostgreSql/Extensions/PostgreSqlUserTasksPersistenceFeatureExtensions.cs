using System.Reflection;
using Flamme.Persistence.EFCore;
using Flamme.Persistence.EFCore.Extensions;
using Flamme.UserTasks.Persistence.EFCore.Features;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace Flamme.UserTasks.Persistence.EFCore.PostgreSql.Extensions;

public static class PostgreSqlUserTasksPersistenceFeatureExtensions
{
    private static Assembly Assembly => typeof(PostgreSqlUserTasksPersistenceFeatureExtensions).Assembly;
    public static EFCoreUserTasksPersistenceFeature UsePostgreSql(this EFCoreUserTasksPersistenceFeature feature, string connectionString, FlammeDbContextOptions? options = null, Action<NpgsqlDbContextOptionsBuilder>? configure = null) => feature.UsePostgreSql(Assembly, connectionString, options, configure);
    public static EFCoreUserTasksPersistenceFeature UsePostgreSql(this EFCoreUserTasksPersistenceFeature feature, Func<IServiceProvider, string> connectionStringFunc, FlammeDbContextOptions? options = null, Action<NpgsqlDbContextOptionsBuilder>? configure = null) => feature.UsePostgreSql(Assembly, connectionStringFunc, options, configure);
}
