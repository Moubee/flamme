using System.Text.Encodings.Web;
using System.Threading.RateLimiting;
using Flamme.Caching.Options;
using Flamme.Common.RecurringTasks;
using Flamme.Expressions.Helpers;
using Flamme.Extensions;
using Flamme.Features.Services;
using Flamme.Http.Options;
using Flamme.Identity.Multitenancy;
using Flamme.Persistence.EFCore.Extensions;
using Flamme.Persistence.EFCore.Modules.Management;
using Flamme.Persistence.EFCore.Modules.Runtime;
using Flamme.Server.Web.Activities;
using Flamme.Server.Web.ActivityHosts;
using Flamme.Server.Web.Filters;
using Flamme.ServiceDefaults;
using Flamme.Tenants;
using Flamme.Tenants.AspNetCore;
using Flamme.Tenants.Extensions;
using Flamme.Workflows;
using Flamme.Workflows.Activities.Flowchart.Extensions;
using Flamme.Workflows.Api;
using Flamme.Workflows.CommitStates.Strategies;
using Flamme.Workflows.IncidentStrategies;
using Flamme.Workflows.LogPersistence;
using Flamme.Workflows.Options;
using Flamme.Workflows.Runtime.Distributed.Extensions;
using Flamme.Workflows.Runtime.Options;
using Flamme.Workflows.Runtime.Tasks;
using JetBrains.Annotations;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

// ReSharper disable RedundantAssignment
const bool useReadOnlyMode = false;
const bool useSignalR = false; // Disabled until Elsa Studio sends authenticated requests.
const bool useMultitenancy = true;
const bool disableVariableWrappers = false;
const string flammeApiRateLimitingPolicy = "elsa-api";
const string httpWorkflowRateLimitingPolicy = "elsa-http-workflows";

ObjectConverter.StrictMode = true;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
var services = builder.Services;
var configuration = builder.Configuration;
var identitySection = configuration.GetSection("Identity");
var identityTokenSection = identitySection.GetSection("Tokens");
var ingressRateLimitingSection = configuration.GetSection("IngressRateLimiting");
var useIngressRateLimiting = ingressRateLimitingSection.GetValue("Enabled", false);
var registerIngressRateLimitingPolicies = ingressRateLimitingSection.GetValue("RegisterReferencePolicies", useIngressRateLimiting);
var configuredAllowLocalDistributedRuntimeLockProvider = configuration.GetValue<bool?>("DistributedRuntime:AllowLocalLockProviderInDistributedRuntime");
var allowLocalDistributedRuntimeLockProvider =
    configuredAllowLocalDistributedRuntimeLockProvider ?? builder.Environment.IsDevelopment();

services
    .AddFlamme(flamme =>
    {
        flamme
            .AddActivitiesFrom<Program>()
            .AddActivityHost<Penguin>()
            .AddWorkflowsFrom<Program>()
            .UseIdentity(identity =>
            {
                identity.TokenOptions += options => identityTokenSection.Bind(options);
                identity.UseConfigurationBasedUserProvider(options => identitySection.Bind(options));
                identity.UseConfigurationBasedApplicationProvider(options => identitySection.Bind(options));
                identity.UseConfigurationBasedRoleProvider(options => identitySection.Bind(options));
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
                management.UseReadOnlyMode(useReadOnlyMode);
            })
            .UseWorkflowRuntime(runtime =>
            {
                runtime.UseEntityFrameworkCore(ef => ef.UsePostgreSql(sp => sp.GetRequiredService<IConfiguration>().GetConnectionString("PostgreSql")
                    ?? throw new InvalidOperationException("ConnectionStrings:PostgreSql is required.")));
                runtime.UseCache();
                runtime.UseDistributedRuntime();
                // This sample host acknowledges single-host file-system locks in development or explicit single-host opt-in.
                runtime.DistributedLockingOptions = options => options.AllowLocalLockProviderInDistributedRuntime = allowLocalDistributedRuntimeLockProvider;
            })
            .UseWorkflowsApi()
            .UseDashboardApi()
            .UseWorkflowRuntimeDashboard()
            .UseFluentStorageProvider()
            .UseScheduling()
            .UseCSharp(options =>
            {
                configuration.GetSection("Scripting:CSharp").Bind(options);
                options.DisableWrappers = disableVariableWrappers;
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
                    configuration.GetSection("Scripting:Python").Bind(options);

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
                http.ConfigureHttpOptions = options => configuration.GetSection("Http").Bind(options);
                http.UseCache();
            });
        
        if(useMultitenancy)
        {
            flamme.UseTenants(tenants =>
            {
                tenants.UseConfigurationBasedTenantsProvider(options => configuration.GetSection("Multitenancy").Bind(options));
                tenants.ConfigureMultitenancy(options => options.TenantResolverPipelineBuilder = new TenantResolverPipelineBuilder()
                    .Append<CurrentUserTenantResolver>());
            });
        }

        ConfigureForTest?.Invoke(flamme);
    });

// Obfuscate HTTP request headers.
services.AddActivityStateFilter<HttpRequestAuthenticationHeaderFilter>();

// Optionally configure recurring tasks using alternative schedules.
services.Configure<RecurringTaskOptions>(options =>
{
    options.Schedule.ConfigureTask<TriggerBookmarkQueueRecurringTask>(TimeSpan.FromSeconds(300));
    options.Schedule.ConfigureTask<PurgeBookmarkQueueRecurringTask>(TimeSpan.FromSeconds(300));
    options.Schedule.ConfigureTask<RestartInterruptedWorkflowsTask>(TimeSpan.FromSeconds(15));
});

services.Configure<RuntimeOptions>(options => { options.InactivityThreshold = TimeSpan.FromSeconds(15); });
services.Configure<BookmarkQueuePurgeOptions>(options => options.Ttl = TimeSpan.FromSeconds(3600));
services.Configure<CachingOptions>(options => options.CacheDuration = TimeSpan.FromDays(1));
services.Configure<IncidentOptions>(options => options.DefaultIncidentStrategy = typeof(ContinueWithIncidentsStrategy));
if (useIngressRateLimiting)
{
    services.PostConfigure<ApiEndpointOptions>(options =>
    {
        if (options.RateLimitingPolicyName == null)
            options.RateLimitingPolicyName = flammeApiRateLimitingPolicy;
    });
    services.PostConfigure<HttpActivityOptions>(options =>
    {
        if (options.RateLimitingPolicyName == null)
            options.RateLimitingPolicyName = httpWorkflowRateLimitingPolicy;
    });
}

services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    if (registerIngressRateLimitingPolicies)
    {
        options.AddFixedWindowLimiter(flammeApiRateLimitingPolicy, limiterOptions =>
        {
            limiterOptions.PermitLimit = ingressRateLimitingSection.GetValue("ApiPermitLimit", 120);
            limiterOptions.Window = TimeSpan.FromSeconds(ingressRateLimitingSection.GetValue("ApiWindowSeconds", 60));
            limiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
            limiterOptions.QueueLimit = ingressRateLimitingSection.GetValue("ApiQueueLimit", 0);
        });
        options.AddFixedWindowLimiter(httpWorkflowRateLimitingPolicy, limiterOptions =>
        {
            limiterOptions.PermitLimit = ingressRateLimitingSection.GetValue("HttpWorkflowPermitLimit", 60);
            limiterOptions.Window = TimeSpan.FromSeconds(ingressRateLimitingSection.GetValue("HttpWorkflowWindowSeconds", 60));
            limiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
            limiterOptions.QueueLimit = ingressRateLimitingSection.GetValue("HttpWorkflowQueueLimit", 0);
        });
    }
});
services
    .AddHealthChecks()
    .AddFlammeReadinessChecks(includeDistributedLocks: true);
services.AddControllers();
services.AddCors(cors => cors.AddDefaultPolicy(policy => policy.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin().WithExposedHeaders("*")));

// Build the web application.
var app = builder.Build();


// Configure the pipeline.
if (app.Environment.IsDevelopment())
    app.UseDeveloperExceptionPage();

// CORS.
app.UseCors();

// Health checks.
app.MapHealthChecks("/health/live", new()
{
    Predicate = _ => false
});
app.MapHealthChecks("/health/ready", new()
{
    Predicate = check => check.Tags.Contains(HealthCheckExtensions.FlammeTag) && check.Tags.Contains(HealthCheckExtensions.ReadinessTag),
    ResultStatusCodes =
    {
        [HealthStatus.Degraded] = StatusCodes.Status503ServiceUnavailable,
        [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
    }
});
app.MapHealthChecks("/", new()
{
    Predicate = _ => false
});

// Flamme API endpoints for designer.
var apiEndpointOptions = app.Services.GetRequiredService<IOptions<ApiEndpointOptions>>().Value;
var routePrefix = apiEndpointOptions.RoutePrefix;
app.MapWorkflowsApi(routePrefix);

// Routing used for SignalR.
app.UseRouting();

app.UseWorkflowsApiRateLimiting(routePrefix, apiEndpointOptions.RateLimitingPolicyName);

// Flamme HTTP Endpoint activities.
var httpActivityOptions = app.Services.GetRequiredService<IOptions<HttpActivityOptions>>().Value;
app.UseWorkflowsRateLimiting(httpActivityOptions.BasePath, httpActivityOptions.RateLimitingPolicyName);
if (useIngressRateLimiting ||
    !string.IsNullOrWhiteSpace(apiEndpointOptions.RateLimitingPolicyName) ||
    !string.IsNullOrWhiteSpace(httpActivityOptions.RateLimitingPolicyName))
    app.UseRateLimiter();

// Security.
app.UseAuthentication();
app.UseAuthorization();

// Multitenancy.
if (useMultitenancy)
    app.UseTenants();

// Captures unhandled exceptions and returns a JSON response.
app.UseJsonSerializationErrorHandler();

app.UseWorkflows();

app.MapControllers();

// Swagger API documentation.
if (app.Environment.IsDevelopment())
{
    app.UseSwaggerUI();
}

// SignalR.
if (useSignalR)
{
    app.UseWorkflowsSignalRHubs();
}

// Run.
await app.RunAsync();

/// <summary>
/// The main entry point for the application made public for end to end testing.
/// </summary>
[UsedImplicitly]
public partial class Program
{
    /// <summary>
    /// Set by the test runner to configure the module for testing.
    /// </summary>
    public static Action<IModule>? ConfigureForTest { get; set; }
}
