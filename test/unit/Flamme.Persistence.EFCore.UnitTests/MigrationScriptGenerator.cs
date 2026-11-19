using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Flamme.Persistence.EFCore.UnitTests;

/// <summary>
/// Generates a migration script offline, through <see cref="IMigrator.GenerateScript"/>, so tests can assert on the
/// SQL a migration produces without opening a database connection.
/// </summary>
internal static class MigrationScriptGenerator
{
    /// <summary>
    /// Builds a <typeparamref name="TDbContext"/> configured by <paramref name="configure"/> and generates the
    /// migration script between <paramref name="fromMigration"/> and <paramref name="toMigration"/>.
    /// </summary>
    /// <param name="configure">Configures the provider (and, through it, the migrations assembly and schema) on the options builder.</param>
    /// <param name="fromMigration">The migration to generate the script from, or <c>null</c> for the initial database state.</param>
    /// <param name="toMigration">The migration to generate the script up to.</param>
    /// <param name="options">The SQL generation options, for example whether the script is idempotent.</param>
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "This JIT-run database test exercises EF schema creation or migrations; production AOT support is tracked separately.")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("AOT", "IL3050", Justification = "This JIT-run database test exercises EF schema creation or migrations; production AOT support is tracked separately.")]
    public static string Generate<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TDbContext>(
        Action<DbContextOptionsBuilder<TDbContext>> configure,
        string? fromMigration,
        string toMigration,
        MigrationsSqlGenerationOptions options)
        where TDbContext : DbContext
    {
        var optionsBuilder = new DbContextOptionsBuilder<TDbContext>();
        configure(optionsBuilder);

        var serviceProvider = new ServiceCollection().BuildServiceProvider();

        using var dbContext = (TDbContext)Activator.CreateInstance(typeof(TDbContext), optionsBuilder.Options, serviceProvider)!;

        return dbContext.GetService<IMigrator>().GenerateScript(fromMigration: fromMigration, toMigration: toMigration, options: options);
    }
}
