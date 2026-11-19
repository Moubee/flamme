using System.IO;
using System.Threading.Tasks;

namespace Elsa.Secrets.UnitTests;

/// <summary>
/// SecretDefaultTenantUniqueness must stamp null TenantId to "" and must not
/// auto-delete duplicate secrets. A preflight lists leftover keys and aborts;
/// CreateIndex then fails loudly if any remain.
/// </summary>
public class SecretDefaultTenantUniquenessMigrationTests
{
    [Test]
    [Arguments("Elsa.Secrets.Persistence.EFCore.PostgreSql")]
    public async Task SecretDefaultTenantUniqueness_StampsNullTenantIdAndDoesNotDeleteDuplicates(string providerProject)
    {
        var migration = FindMigration(providerProject);

        await Assert.That(migration).Contains("TenantId");
        await Assert.That(migration).Contains("IS NULL");
        await Assert.That(migration).DoesNotContain("DELETE FROM");
        await Assert.That(migration).DoesNotContain("DeleteData");
        await Assert.That(migration).DoesNotContain("COALESCE");
    }

    [Test]
    [Arguments("Elsa.Secrets.Persistence.EFCore.PostgreSql", "RAISE EXCEPTION")]
    public async Task SecretDefaultTenantUniqueness_PreflightsDuplicateKeys(string providerProject, string abortKeyword)
    {
        var migration = FindMigration(providerProject);

        await Assert.That(migration).Contains(abortKeyword);
        await Assert.That(migration).Contains("COUNT(*)");
        await Assert.That(migration).Contains("Duplicate keys");
        await Assert.That(migration).Contains("HAVING COUNT(*) > 1");
    }

    private static string FindMigration(string providerProject)
    {
        if (Path.IsPathRooted(providerProject))
            throw new ArgumentException("Provider project must be a relative path.", nameof(providerProject));

        var repoRoot = FindRepoRoot();
        var secretsDir = Path.Combine(repoRoot, "src", "modules", providerProject, "Migrations", "Secrets");
        var path = Directory.GetFiles(secretsDir, "*SecretDefaultTenantUniqueness.cs")
            .Single(file => !file.EndsWith(".Designer.cs", StringComparison.Ordinal));
        return File.ReadAllText(path);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Join(directory.FullName, "Elsa.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find Elsa.slnx from the test output directory.");
    }
}
