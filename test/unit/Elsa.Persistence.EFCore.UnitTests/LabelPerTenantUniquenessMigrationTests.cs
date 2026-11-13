namespace Elsa.Persistence.EFCore.UnitTests;

/// <summary>
/// PerTenantLabelUniqueness must stamp null TenantId to "" and must not
/// auto-delete duplicate labels (associations would be at risk). A preflight
/// lists leftover keys and aborts; CreateIndex then fails loudly if any remain.
/// PostgreSQL keeps the provider column types.
/// </summary>
public class LabelPerTenantUniquenessMigrationTests
{
    [Test]
    [Arguments("Elsa.Persistence.EFCore.PostgreSql")]
    public async Task PerTenantLabelUniqueness_StampsNullTenantIdAndDoesNotDeleteDuplicates(string providerProject)
    {
        var migration = FindMigration(providerProject);

        await Assert.That(migration).Contains("TenantId").WithComparison(StringComparison.Ordinal);
        await Assert.That(migration).Contains("IS NULL").WithComparison(StringComparison.Ordinal);
        await Assert.That(migration).DoesNotContain("DELETE FROM").WithComparison(StringComparison.OrdinalIgnoreCase);
        await Assert.That(migration).DoesNotContain("DeleteData").WithComparison(StringComparison.OrdinalIgnoreCase);
        await Assert.That(migration).DoesNotContain("COALESCE").WithComparison(StringComparison.OrdinalIgnoreCase);
    }

    [Test]
    [Arguments("Elsa.Persistence.EFCore.PostgreSql", "RAISE EXCEPTION")]
    public async Task PerTenantLabelUniqueness_PreflightsDuplicateKeys(string providerProject, string abortKeyword)
    {
        var migration = FindMigration(providerProject);

        await Assert.That(migration).Contains(abortKeyword).WithComparison(StringComparison.Ordinal);
        await Assert.That(migration).Contains("COUNT(*)").WithComparison(StringComparison.Ordinal);
        await Assert.That(migration).Contains("Duplicate keys").WithComparison(StringComparison.OrdinalIgnoreCase);
        await Assert.That(migration).Contains("HAVING COUNT(*) > 1").WithComparison(StringComparison.Ordinal);
    }

    [Test]
    [Arguments("Elsa.Persistence.EFCore.PostgreSql")]
    public async Task PerTenantLabelUniqueness_KeepsUnboundedProviderColumnTypes(string providerProject)
    {
        var migration = FindMigration(providerProject);

        await Assert.That(migration).DoesNotContain("AlterColumn").WithComparison(StringComparison.Ordinal);
        await Assert.That(migration).DoesNotContain("varchar(255)").WithComparison(StringComparison.OrdinalIgnoreCase);
        await Assert.That(migration).DoesNotContain("nvarchar(255)").WithComparison(StringComparison.OrdinalIgnoreCase);
    }

    private static string FindMigration(string providerProject)
    {
        var repoRoot = FindRepoRoot();
        var labelsDir = Path.Combine(repoRoot, "src", "modules", providerProject, "Migrations", "Labels");
        var path = Directory.GetFiles(labelsDir, "*PerTenantLabelUniqueness.cs")
            .Single(file => !file.EndsWith(".Designer.cs", StringComparison.Ordinal));
        return File.ReadAllText(path);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Elsa.sln")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find Elsa.sln from the test output directory.");
    }
}
