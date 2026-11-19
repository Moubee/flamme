using System.Reflection;
using Elsa.Persistence.EFCore.Extensions;
using Elsa.Testing.Shared.Databases;
using Elsa.UserTasks.Persistence.ConformanceTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.UserTasks.Persistence.ConformanceTests.Providers;

/// <summary>
/// PostgreSQL runs by default in an isolated test database. ELSA_USERTASKS_TEST_POSTGRES can select
/// an operator-supplied disposable database instead; that database is migrated but never dropped.
/// </summary>
public sealed class PostgreSqlUserTaskStoreFixture : EFCoreUserTaskStoreFixture
{
    private readonly PostgreSqlTestDatabase _database = new();
    private string? _connectionString;

    public PostgreSqlUserTaskStoreFixture() : base(ConformanceProviders.PostgreSql)
    {
    }

    protected override Assembly MigrationsAssembly => typeof(UserTasks.Persistence.EFCore.PostgreSql.Extensions.PostgreSqlUserTasksPersistenceFeatureExtensions).Assembly;
    protected override string ResolveConnectionString() => _connectionString
        ?? throw new InvalidOperationException("The PostgreSQL conformance database has not been initialized.");

    protected override async Task ActivateCoreAsync()
    {
        var configured = Environment.GetEnvironmentVariable("ELSA_USERTASKS_TEST_POSTGRES");
        if (configured is null)
        {
            await _database.OpenAsync();
            _connectionString = _database.ConnectionString;
        }
        else
        {
            _connectionString = Provider.ConnectionString;
        }

        await base.ActivateCoreAsync();
    }

    protected override async Task DisposeCoreAsync()
    {
        try
        {
            await base.DisposeCoreAsync();
        }
        finally
        {
            await _database.DisposeAsync();
        }
    }

    protected override void ConfigureServices(IServiceCollection services) => services.AddPostgreSqlEntityModelCreatingHandlers();
    protected override void ConfigureProvider(DbContextOptionsBuilder builder, string connectionString) => builder.UseElsaPostgreSql(MigrationsAssembly, connectionString);
}
