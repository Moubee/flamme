using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Flamme.Persistence.EFCore;

/// <summary>
/// Provides options for configuring Flamme's Entity Framework Core integration.
/// </summary>
public static class FlammeDbContextOptionsExtensions
{
    /// <summary>
    /// Installs a custom extension for Flamme's Entity Framework Core integration.
    /// </summary>
    /// <param name="optionsBuilder">The options builder to install the extension on.</param>
    /// <param name="options">The options to install.</param>
    public static DbContextOptionsBuilder UseFlammeDbContextOptions(this DbContextOptionsBuilder optionsBuilder, FlammeDbContextOptions? options)
    {
        optionsBuilder.AddFlammeOptions(options);
        optionsBuilder.ReplaceService<IMigrationsAssembly, DbSchemaAwareMigrationAssembly>();
        return optionsBuilder;
    }

    internal static void AddFlammeOptions(this DbContextOptionsBuilder builder, FlammeDbContextOptions? options) =>
        ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(new FlammeDbContextOptionsExtension(options));

    public static string GetMigrationsAssemblyName(this FlammeDbContextOptions? options, Assembly migrationsAssembly) => options?.MigrationsAssemblyName ?? migrationsAssembly.GetName().Name!;
    public static string GetMigrationsHistoryTableName(this FlammeDbContextOptions? options) => options?.MigrationsHistoryTableName ?? FlammeDbContextBase.MigrationsHistoryTable;
    public static string GetSchemaName(this FlammeDbContextOptions? options) => options?.SchemaName ?? FlammeDbContextBase.FlammeSchema;
}

internal sealed class FlammeModelCacheKeyFactory : IModelCacheKeyFactory
{
    // Schema, tenant visibility and the owner concurrency token are fixed when EF builds a model.
    public object Create(DbContext context, bool designTime) => context is FlammeDbContextBase flamme
        ? (context.GetType(), designTime, flamme.Schema, flamme.TenancyEnabled)
        : new ModelCacheKey(context, designTime);
}
