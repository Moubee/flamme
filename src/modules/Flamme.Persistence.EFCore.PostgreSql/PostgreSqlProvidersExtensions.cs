using System.Reflection;
using Flamme.Extensions;
using Flamme.Persistence.EFCore.PostgreSql.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

// ReSharper disable once CheckNamespace
namespace Flamme.Persistence.EFCore.Extensions;

/// <summary>
/// Provides extensions to configure EF Core to use PostgreSQL.
/// </summary>
public static class PostgreSqlProvidersExtensions
{
    private static Assembly Assembly => typeof(PostgreSqlProvidersExtensions).Assembly;

    public static IServiceCollection AddPostgreSqlEntityModelCreatingHandlers(this IServiceCollection services) =>
        services.TryAddScopedImplementation<IEntityModelCreatingHandler, SetupForPostgreSql>();

    public static TFeature UsePostgreSql<TFeature, TDbContext>(this PersistenceFeatureBase<TFeature, TDbContext> feature,
        string connectionString,
        FlammeDbContextOptions? options = null,
        Action<NpgsqlDbContextOptionsBuilder>? configure = null)
        where TDbContext : FlammeDbContextBase
        where TFeature : PersistenceFeatureBase<TFeature, TDbContext>
    {
        return feature.UsePostgreSql(Assembly, connectionString, options, configure);
    }

    public static TFeature UsePostgreSql<TFeature, TDbContext>(this PersistenceFeatureBase<TFeature, TDbContext> feature,
        Func<IServiceProvider, string> connectionStringFunc,
        FlammeDbContextOptions? options = null,
        Action<NpgsqlDbContextOptionsBuilder>? configure = null)
        where TDbContext : FlammeDbContextBase
        where TFeature : PersistenceFeatureBase<TFeature, TDbContext>
    {
        return feature.UsePostgreSql(Assembly, connectionStringFunc, options, configure);
    }

    public static TFeature UsePostgreSql<TFeature, TDbContext>(this PersistenceFeatureBase<TFeature, TDbContext> feature,
        Assembly migrationsAssembly,
        string connectionString,
        FlammeDbContextOptions? options = null,
        Action<NpgsqlDbContextOptionsBuilder>? configure = null)
        where TDbContext : FlammeDbContextBase
        where TFeature : PersistenceFeatureBase<TFeature, TDbContext>
    {
        return feature.UsePostgreSql(migrationsAssembly, _ => connectionString, options, configure);
    }

    public static TFeature UsePostgreSql<TFeature, TDbContext>(this PersistenceFeatureBase<TFeature, TDbContext> feature,
        Assembly migrationsAssembly,
        Func<IServiceProvider, string> connectionStringFunc,
        FlammeDbContextOptions? options = null,
        Action<NpgsqlDbContextOptionsBuilder>? configure = null)
        where TDbContext : FlammeDbContextBase
        where TFeature : PersistenceFeatureBase<TFeature, TDbContext>
    {
        feature.Module.Services.AddPostgreSqlEntityModelCreatingHandlers();
        feature.DbContextOptionsBuilder = (sp, db) => db.UseFlammePostgreSql(migrationsAssembly, connectionStringFunc(sp), options, configure: configure);
        return (TFeature)feature;
    }
}
