using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Flamme.Expressions.Helpers;
using Flamme.Identity.Models;
using Flamme.Persistence.EFCore.Modules.Management;
using Flamme.Persistence.EFCore.Modules.Runtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Flamme.Workflows.PerformanceTests.WorkflowStore;

internal sealed class WorkflowHostFixture : IAsyncDisposable
{
    private static readonly string[] TenantIds = ["", "tenant-1", "tenant-2"];
    private readonly Dictionary<string, HttpClient> _clients = new();
    private readonly bool _previousStrictMode = ObjectConverter.StrictMode;
    private readonly PostgreSqlContainer _database = WorkflowPostgres.Create();
    private DirectoryInfo? _directory;
    private WorkflowHostFactory? _factory;

    private WorkflowHostFixture() { }

    public HttpClient Client => GetClient("");
    public HttpClient GetClient(string tenantId) => _clients[tenantId];
    public string ConnectionString => _database.GetConnectionString();

    public static async Task<WorkflowHostFixture> StartAsync()
    {
        var fixture = new WorkflowHostFixture();
        try
        {
            await fixture.InitializeAsync();
            return fixture;
        }
        catch
        {
            await fixture.DisposeAsync();
            throw;
        }
    }

    private async Task InitializeAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var cancellationToken = timeout.Token;
        await _database.StartAsync(cancellationToken);
        _directory = Directory.CreateTempSubdirectory("flamme-workflow-benchmark-");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), Path.Combine(_directory.FullName, "appsettings.json"));

        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        _factory = new WorkflowHostFactory(_directory, ConnectionString, TenantIds, password);
        _factory.UseKestrel(0);
        _factory.StartServer();
        using var probe = _factory.CreateDefaultClient();
        using var readiness = await probe.GetAsync("/health/ready", cancellationToken);
        readiness.EnsureSuccessStatusCode();
        var api = new Uri(probe.BaseAddress!, "/elsa/api/");
        const string instances = "workflow-instances?pageSize=1";
        using var anonymous = await probe.GetAsync(new Uri(api, instances), cancellationToken);
        if (anonymous.StatusCode != HttpStatusCode.Unauthorized)
            throw new InvalidOperationException($"Anonymous workflow instance query returned {anonymous.StatusCode}.");

        foreach (var tenantId in TenantIds)
        {
            var client = _factory.CreateDefaultClient();
            _clients.Add(tenantId, client);
            client.BaseAddress = api;
            var username = WorkflowHostFactory.UserName(tenantId);
            using var response = await client.PostAsJsonAsync("identity/login", new { username, password }, cancellationToken);
            response.EnsureSuccessStatusCode();
            var login = await response.Content.ReadFromJsonAsync<LoginResponse>(cancellationToken);
            if (login is not { IsAuthenticated: true, AccessToken: { Length: > 0 } token })
                throw new InvalidOperationException($"Reference host login failed for {username}.");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var authorized = await client.GetAsync(instances, cancellationToken);
            authorized.EnsureSuccessStatusCode();
        }

        await using var scope = _factory.Services.CreateAsyncScope();
        await VerifyMigrationsAsync<ManagementFlammeDbContext>(scope.ServiceProvider, cancellationToken);
        await VerifyMigrationsAsync<RuntimeFlammeDbContext>(scope.ServiceProvider, cancellationToken);
    }

    private static async Task VerifyMigrationsAsync<TContext>(IServiceProvider services, CancellationToken cancellationToken)
        where TContext : DbContext
    {
        var factory = services.GetRequiredService<IDbContextFactory<TContext>>();
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        if (!context.Database.GetMigrations().Any() || (await context.Database.GetPendingMigrationsAsync(cancellationToken)).Any())
            throw new InvalidOperationException($"Reference host did not apply all {typeof(TContext).Name} migrations.");
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_factory != null)
                await _factory.DisposeAsync(); // The factory also owns its HTTP clients.
        }
        finally
        {
            ObjectConverter.StrictMode = _previousStrictMode;
            try
            {
                await _database.DisposeAsync();
            }
            finally
            {
                if (_directory is { Exists: true })
                    _directory.Delete(true);
            }
        }
    }
}
