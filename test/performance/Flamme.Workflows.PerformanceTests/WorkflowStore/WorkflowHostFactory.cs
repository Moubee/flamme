using System.Security.Cryptography;
using Flamme.Identity.Services;
using Flamme.Server.Web;
using Medallion.Threading;
using Medallion.Threading.FileSystem;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Flamme.Workflows.PerformanceTests.WorkflowStore;

internal sealed class WorkflowHostFactory(
    DirectoryInfo directory, string connectionString, IReadOnlyList<string> tenantIds, string password)
    : WebApplicationFactory<ClassicServerHost>
{
    internal static string UserName(string tenantId) => tenantId.Length == 0 ? "benchmark-default" : $"benchmark-{tenantId}";

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var settings = new Dictionary<string, string?>
        {
            [HostDefaults.EnvironmentKey] = Environments.Development,
            [HostDefaults.ContentRootKey] = directory.FullName,
            ["TEST_CONTENTROOT_FLAMME_SERVER_WEB"] = directory.FullName,
            ["HostBuilder:reloadConfigOnChange"] = "false",
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "",
            ["Scripting:Python:AllowHostCodeExecution"] = "false",
            ["ConnectionStrings:PostgreSql"] = connectionString,
            ["Http:BaseUrl"] = "http://127.0.0.1",
            ["Identity:Tokens:SigningKey"] = Convert.ToHexString(RandomNumberGenerator.GetBytes(48))
        };
        var hasher = new DefaultSecretHasher();
        for (var i = 0; i < tenantIds.Count; i++)
        {
            var tenant = $"Multitenancy:Tenants:{i}:Configuration";
            settings[$"{tenant}:ConnectionStrings:PostgreSql"] = connectionString;
            settings[$"{tenant}:Http:Host"] = "127.0.0.1";
            var user = $"Identity:Users:{i}";
            var hash = hasher.HashSecret(password);
            settings[$"{user}:Id"] = Guid.NewGuid().ToString("N");
            settings[$"{user}:Name"] = UserName(tenantIds[i]);
            settings[$"{user}:TenantId"] = tenantIds[i];
            settings[$"{user}:Roles:0"] = "admin";
            settings[$"{user}:HashedPassword"] = hash.EncodeSecret();
            settings[$"{user}:HashedPasswordSalt"] = hash.EncodeSalt();
        }

        // The minimal host reads configuration during Program, before ConfigureWebHost callbacks.
        builder.ConfigureHostConfiguration(config => config.AddInMemoryCollection(settings));
        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IDistributedLockProvider>();
            services.AddSingleton<IDistributedLockProvider>(new FileDistributedSynchronizationProvider(directory.CreateSubdirectory("locks")));
            services.AddDataProtection().PersistKeysToFileSystem(directory.CreateSubdirectory("keys"));
        });
    }
}
