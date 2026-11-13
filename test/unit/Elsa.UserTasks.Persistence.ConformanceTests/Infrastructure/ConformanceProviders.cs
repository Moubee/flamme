namespace Elsa.UserTasks.Persistence.ConformanceTests.Infrastructure;

/// <summary>
/// The persistence providers the conformance suite knows about, and whether this run can reach them.
///
/// A provider that cannot be reached is never silently omitted: it is reported by
/// <see cref="ConformanceCoverageTests"/> and every one of its tests is reported as skipped, with the
/// provider-specific reason. A provider that reads as "passed" must actually have run.
/// </summary>
public static class ConformanceProviders
{
    public const string InMemory = "InMemory";
    public const string PostgreSql = "EFCore.PostgreSql";

    /// <summary>Every provider, in report order. Availability is resolved once per test run.</summary>
    public static IReadOnlyList<ConformanceProvider> All { get; } =
    [
        Always(InMemory, "Elsa.UserTasks in-process stores"),
        RequiredPostgreSql()
    ];

    public static ConformanceProvider Get(string name) =>
        All.FirstOrDefault(x => x.Name == name) ?? throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown conformance provider.");

    private static ConformanceProvider Always(string name, string description) => new(name, description, null, null);

    private static ConformanceProvider RequiredPostgreSql()
    {
        const string variable = "ELSA_USERTASKS_TEST_POSTGRES";
        var configured = Environment.GetEnvironmentVariable(variable);
        var reason = configured is not null && string.IsNullOrWhiteSpace(configured)
            ? $"{variable} is set but empty or whitespace. Supply a PostgreSQL connection string or unset it to use an isolated test database."
            : null;
        return new(PostgreSql, "Elsa.UserTasks.Persistence.EFCore over PostgreSQL", variable, reason);
    }

}

/// <param name="Name">The stable provider key used by <see cref="ConformanceProviderAttribute"/>.</param>
/// <param name="Description">Human-readable description used in the coverage report.</param>
/// <param name="ConnectionStringVariable">The environment variable carrying an optional explicit database connection string.</param>
/// <param name="SkipReason">Null when the provider runs; otherwise the reason it does not.</param>
public sealed record ConformanceProvider(string Name, string Description, string? ConnectionStringVariable, string? SkipReason)
{
    public bool IsAvailable => SkipReason is null;

    public string ConnectionString
    {
        get
        {
            if (ConnectionStringVariable is null)
                throw new InvalidOperationException($"Provider '{Name}' is not configured by a connection string.");

            var value = Environment.GetEnvironmentVariable(ConnectionStringVariable);
            return !string.IsNullOrWhiteSpace(value)
                ? value
                : throw new InvalidOperationException($"Provider '{Name}' requires {ConnectionStringVariable} to contain a non-whitespace connection string.");
        }
    }
}
