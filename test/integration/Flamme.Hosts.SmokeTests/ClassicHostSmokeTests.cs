using Flamme.Expressions.Helpers;
using Flamme.Server.Web;

namespace Flamme.Hosts.SmokeTests;

/// <summary>Flamme.Server.Web, which registers its modules through the classic <c>Features/</c> path.</summary>
[InheritsTests]
[NotInParallel(HostSmokeTestConstraints.EntryPointProcessState)]
public class ClassicHostSmokeTests : HostSmokeTests<ClassicHostWebApplicationFactory, ClassicServerHost>
{
    private bool _originalStrictMode;

    /// <inheritdoc />
    protected override IReadOnlyCollection<string> GatedRoutes =>
    [
        "/elsa/api/workflow-definitions",
        "/elsa/api/workflow-instances",
        "/elsa/api/identity/roles",
        "/elsa/api/identity/permissions",
        "/elsa/api/dashboard/overview"
    ];

    protected override IReadOnlyDictionary<string, string?> CreateTestConfiguration()
    {
        var connectionString = ConnectionString;
        return new Dictionary<string, string?>
        {
            ["ConnectionStrings:PostgreSql"] = connectionString,
            ["Multitenancy:Tenants:0:Configuration:ConnectionStrings:PostgreSql"] = connectionString,
            ["Multitenancy:Tenants:1:Configuration:ConnectionStrings:PostgreSql"] = connectionString,
            ["Multitenancy:Tenants:2:Configuration:ConnectionStrings:PostgreSql"] = connectionString,
            ["Multitenancy:Tenants:0:Configuration:Http:Host"] = "localhost",
            ["Multitenancy:Tenants:1:Configuration:Http:Host"] = "localhost",
            ["Multitenancy:Tenants:2:Configuration:Http:Host"] = "localhost",
            ["Http:BaseUrl"] = "http://localhost",
            ["Scripting:Python:AllowHostCodeExecution"] = "false"
        };
    }

    protected override void CaptureEntryPointProcessState()
    {
        base.CaptureEntryPointProcessState();
        _originalStrictMode = ObjectConverter.StrictMode;
    }

    protected override ValueTask RestoreEntryPointProcessStateAsync()
    {
        ObjectConverter.StrictMode = _originalStrictMode;
        return base.RestoreEntryPointProcessStateAsync();
    }
}

public sealed class ClassicHostWebApplicationFactory : HostWebApplicationFactory<ClassicServerHost>;
