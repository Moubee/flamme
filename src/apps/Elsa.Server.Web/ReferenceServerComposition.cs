using System.Text.Encodings.Web;
using Elsa.Extensions;
using Elsa.Features.Services;
using Elsa.Identity.Multitenancy;
using Elsa.Persistence.EFCore.Extensions;
using Elsa.Persistence.EFCore.Modules.Management;
using Elsa.Persistence.EFCore.Modules.Runtime;
using Elsa.Server.Web.ActivityHosts;
using Elsa.Tenants;
using Elsa.Tenants.Extensions;
using Elsa.Workflows.Activities.Flowchart.Extensions;
using Elsa.Workflows.CommitStates.Strategies;
using Elsa.Workflows.LogPersistence;
using Elsa.Workflows.Runtime.Distributed.Extensions;

namespace Elsa.Server.Web;

/// <summary>Composes the feature graph used by the reference server.</summary>
public sealed class ReferenceServerComposition(WebApplicationBuilder builder)
{
    internal const bool UseMultitenancy = true;
    private const bool UseReadOnlyMode = false;
    private const bool DisableVariableWrappers = false;
    private readonly IServiceCollection _services = builder.Services;
    private readonly IConfiguration _configuration = builder.Configuration;
    private readonly IConfigurationSection _identitySection = builder.Configuration.GetSection("Identity");
    private readonly IConfigurationSection _identityTokenSection = builder.Configuration.GetSection("Identity:Tokens");
    private readonly bool _allowLocalDistributedRuntimeLockProvider =
        builder.Configuration.GetValue<bool?>("DistributedRuntime:AllowLocalLockProviderInDistributedRuntime") ?? builder.Environment.IsDevelopment();

    /// <summary>Creates, configures and applies the module to the server's services.</summary>
    public IModule Apply()
    {
        return _services.AddElsa(elsa =>
        {
            elsa
                .AddActivitiesFrom<Program>()
                .AddActivityHost<Penguin>()
                .AddWorkflowsFrom<Program>()
                .UseIdentity(identity =>
                {
                    identity.TokenOptions += options => _identityTokenSection.Bind(options);
                    identity.UseConfigurationBasedUserProvider(options => _identitySection.Bind(options));
                    identity.UseConfigurationBasedApplicationProvider(options => _identitySection.Bind(options));
                    identity.UseConfigurationBasedRoleProvider(options => _identitySection.Bind(options));
                })
                .UseDefaultAuthentication()
                .UseWorkflows(workflows =>
                {
                    workflows.UseCommitStrategies(strategies =>
                    {
                        strategies.AddStandardStrategies();
                        strategies.Add("Every 10 seconds", new PeriodicWorkflowStrategy(TimeSpan.FromSeconds(10)));
                    });
                })
                .UseFlowchart(flowchart => flowchart.UseTokenBasedExecution())
                .UseWorkflowManagement(management =>
                {
                    management.UseEntityFrameworkCore(ef => ef.UsePostgreSql(sp => sp.GetRequiredService<IConfiguration>().GetConnectionString("PostgreSql")
                        ?? throw new InvalidOperationException("ConnectionStrings:PostgreSql is required.")));
                    management.SetDefaultLogPersistenceMode(LogPersistenceMode.Inherit);
                    management.UseCache();
                    management.UseReadOnlyMode(UseReadOnlyMode);
                })
                .UseWorkflowRuntime(runtime =>
                {
                    runtime.UseEntityFrameworkCore(ef => ef.UsePostgreSql(sp => sp.GetRequiredService<IConfiguration>().GetConnectionString("PostgreSql")
                        ?? throw new InvalidOperationException("ConnectionStrings:PostgreSql is required.")));
                    runtime.UseCache();
                    runtime.UseDistributedRuntime();
                    // This sample host acknowledges single-host file-system locks in development or explicit single-host opt-in.
                    runtime.DistributedLockingOptions = options => options.AllowLocalLockProviderInDistributedRuntime = _allowLocalDistributedRuntimeLockProvider;
                })
                .UseWorkflowsApi()
                .UseDashboardApi()
                .UseWorkflowRuntimeDashboard()
                .UseFluentStorageProvider()
                .UseScheduling()
                .UseCSharp(options =>
                {
                    _configuration.GetSection("Scripting:CSharp").Bind(options);
                    options.DisableWrappers = DisableVariableWrappers;
                    options.AppendScript("string Greet(string name) => $\"Hello {name}!\";");
                    options.AppendScript("string SayHelloWorld() => Greet(\"World\");");
                })
                .UseJavaScript(options =>
                {
                    options.AllowClrAccess = true;
                    options.ConfigureEngine(engine =>
                    {
                        engine.Execute("function greet(name) { return `Hello ${name}!`; }");
                        engine.Execute("function sayHelloWorld() { return greet('World'); }");
                    });
                })
                .UsePython(python =>
                {
                    python.PythonOptions += options =>
                    {
                        // Make sure to configure the path to the python DLL. E.g. /opt/homebrew/Cellar/python@3.11/3.11.6_1/Frameworks/Python.framework/Versions/3.11/bin/python3.11
                        // alternatively, you can set the PYTHONNET_PYDLL environment variable.
                        _configuration.GetSection("Scripting:Python").Bind(options);

                        options.AddScript(sb =>
                        {
                            sb.AppendLine("def greet():");
                            sb.AppendLine("    return \"Hello, welcome to Python!\"");
                        });
                    };
                })
                .UseLiquid(liquid => liquid.FluidOptions = options => options.Encoder = HtmlEncoder.Default)
                .UseHttp(http =>
                {
                    http.ConfigureHttpOptions = options => _configuration.GetSection("Http").Bind(options);
                    http.UseCache();
                });

            if (UseMultitenancy)
            {
                elsa.UseTenants(tenants =>
                {
                    tenants.UseConfigurationBasedTenantsProvider(options => _configuration.GetSection("Multitenancy").Bind(options));
                    tenants.ConfigureMultitenancy(options => options.TenantResolverPipelineBuilder = new TenantResolverPipelineBuilder()
                        .Append<CurrentUserTenantResolver>());
                });
            }
        });
    }
}
