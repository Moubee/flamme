using System.Threading.RateLimiting;
using Elsa.Caching.Options;
using Elsa.Common.RecurringTasks;
using Elsa.Expressions.Helpers;
using Elsa.Extensions;
using Elsa.Http.Options;
using Elsa.Server.Web;
using Elsa.Server.Web.Filters;
using Elsa.ServiceDefaults;
using Elsa.Tenants.AspNetCore;
using Elsa.Workflows;
using Elsa.Workflows.Api;
using Elsa.Workflows.IncidentStrategies;
using Elsa.Workflows.Options;
using Elsa.Workflows.Runtime.Options;
using Elsa.Workflows.Runtime.Tasks;
using JetBrains.Annotations;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

// ReSharper disable RedundantAssignment
const bool useSignalR = false; // Disabled until Elsa Studio sends authenticated requests.
const string elsaApiRateLimitingPolicy = "elsa-api";
const string httpWorkflowRateLimitingPolicy = "elsa-http-workflows";

ObjectConverter.StrictMode = true;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
var services = builder.Services;
var configuration = builder.Configuration;
var ingressRateLimitingSection = configuration.GetSection("IngressRateLimiting");
var useIngressRateLimiting = ingressRateLimitingSection.GetValue("Enabled", false);
var registerIngressRateLimitingPolicies = ingressRateLimitingSection.GetValue("RegisterReferencePolicies", useIngressRateLimiting);

new ReferenceServerComposition(builder).Apply();

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
            options.RateLimitingPolicyName = elsaApiRateLimitingPolicy;
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
        options.AddFixedWindowLimiter(elsaApiRateLimitingPolicy, limiterOptions =>
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
    .AddElsaReadinessChecks(includeDistributedLocks: true);
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
    Predicate = check => check.Tags.Contains(HealthCheckExtensions.ElsaTag) && check.Tags.Contains(HealthCheckExtensions.ReadinessTag),
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

// Elsa API endpoints for designer.
var apiEndpointOptions = app.Services.GetRequiredService<IOptions<ApiEndpointOptions>>().Value;
var routePrefix = apiEndpointOptions.RoutePrefix;
app.MapWorkflowsApi(routePrefix);

// Routing used for SignalR.
app.UseRouting();

app.UseWorkflowsApiRateLimiting(routePrefix, apiEndpointOptions.RateLimitingPolicyName);

// Elsa HTTP Endpoint activities.
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
if (ReferenceServerComposition.UseMultitenancy)
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
/// The entry point for the reference server.
/// </summary>
[UsedImplicitly]
public partial class Program;
