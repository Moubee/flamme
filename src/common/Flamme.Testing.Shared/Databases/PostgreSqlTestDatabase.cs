using Npgsql;
using Testcontainers.PostgreSql;

namespace Flamme.Testing.Shared.Databases;

/// <summary>Creates an isolated PostgreSQL database, using a configured server or a shared test container.</summary>
public sealed class PostgreSqlTestDatabase : IAsyncDisposable, IDisposable
{
    private static readonly Lazy<Task<PostgreSqlContainer>> Container = new(StartContainerAsync);
    private readonly string _database = $"elsa_test_{Guid.NewGuid():N}";
    private NpgsqlConnection? _connection;
    private string? _adminConnectionString;
    private string? _connectionString;

    public NpgsqlConnection Connection
    {
        get
        {
            Open();
            return _connection!;
        }
    }

    public string ConnectionString
    {
        get
        {
            Open();
            return _connectionString!;
        }
    }

    public void Open() => OpenAsync().GetAwaiter().GetResult();

    public async Task OpenAsync()
    {
        if (_connection is not null)
            return;

        var configured = Environment.GetEnvironmentVariable("ELSA_TEST_POSTGRES");
        var server = string.IsNullOrWhiteSpace(configured)
            ? (await Container.Value).GetConnectionString()
            : configured;
        var admin = new NpgsqlConnectionStringBuilder(server) { Database = "postgres", Pooling = false };
        _adminConnectionString = admin.ConnectionString;
        await using (var connection = new NpgsqlConnection(_adminConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE \"{_database}\"";
            await command.ExecuteNonQueryAsync();
        }

        var target = new NpgsqlConnectionStringBuilder(server) { Database = _database, Pooling = false };
        _connectionString = target.ConnectionString;
        _connection = new NpgsqlConnection(_connectionString);
        await _connection.OpenAsync();
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        if (_adminConnectionString is null)
            return;

        if (_connection is not null)
            await _connection.DisposeAsync();
        await using var admin = new NpgsqlConnection(_adminConnectionString);
        await admin.OpenAsync();
        await using var command = admin.CreateCommand();
        command.CommandText = $"DROP DATABASE IF EXISTS \"{_database}\" WITH (FORCE)";
        await command.ExecuteNonQueryAsync();
        _adminConnectionString = null;
        _connection = null;
    }

    private static async Task<PostgreSqlContainer> StartContainerAsync()
    {
        var container = new PostgreSqlBuilder().WithImage("postgres:17-alpine").Build();
        await container.StartAsync();
        return container;
    }
}
