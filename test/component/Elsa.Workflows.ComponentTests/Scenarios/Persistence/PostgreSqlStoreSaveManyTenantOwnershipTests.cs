using Elsa.Persistence.EFCore.UnitTests;
using Testcontainers.PostgreSql;
using TUnit.Core.Interfaces;

namespace Elsa.Workflows.ComponentTests.Scenarios.Persistence;

[InheritsTests]
[ClassDataSource<PostgreSqlStoreSaveManyFixture>(Shared = SharedType.PerClass)]
[NotInParallel("StoreSaveMany:PostgreSql")]
public sealed class PostgreSqlStoreSaveManyTenantOwnershipTests : StoreSaveManyTenantOwnershipTests
{
    private readonly PostgreSqlStoreSaveManyFixture _fixture;

    public PostgreSqlStoreSaveManyTenantOwnershipTests(PostgreSqlStoreSaveManyFixture fixture)
    {
        _fixture = fixture;
    }

    protected override Task<OwnershipStoreScenario> CreateScenarioAsync(string tenantId, bool tenantsEnabled = true) =>
        OwnershipStoreScenario.CreatePostgreSqlAsync(_fixture.ConnectionString, tenantId, tenantsEnabled);
}

public sealed class PostgreSqlStoreSaveManyFixture : IAsyncInitializer, IAsyncDisposable
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();

    public ValueTask DisposeAsync() => _container.DisposeAsync();
}
