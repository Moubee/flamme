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
        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(new FlammeDbContextOptionsExtension(options));
        optionsBuilder.ReplaceService<IMigrationsAssembly, DbSchemaAwareMigrationAssembly>();
        return optionsBuilder;
    }

    public static string GetMigrationsAssemblyName(this FlammeDbContextOptions? options, Assembly migrationsAssembly) => options?.MigrationsAssemblyName ?? migrationsAssembly.GetName().Name!;
    public static string GetMigrationsHistoryTableName(this FlammeDbContextOptions? options) => options?.MigrationsHistoryTableName ?? FlammeDbContextBase.MigrationsHistoryTable;
    public static string GetSchemaName(this FlammeDbContextOptions? options) => options?.SchemaName ?? FlammeDbContextBase.FlammeSchema;
}
