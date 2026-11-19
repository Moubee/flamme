using Flamme.Extensions;
using Flamme.Tenants.AspNetCore;
using Flamme.Workflows.Activities.Flowchart.Extensions;
using Flamme.Workflows.Api;
using Flamme.Workflows.CommitStates.Strategies;
using Flamme.Workflows.ComponentTests.Host;
using Flamme.Workflows.LogPersistence;
using Flamme.Workflows.Runtime.Distributed.Extensions;

var builder = WebApplication.CreateBuilder(args);
var services = builder.Services;

// TUnit captures application logs in each result. Information-level EF command logging from
// hundreds of isolated hosts otherwise dominates both runtime and report size.
builder.Logging.SetMinimumLevel(LogLevel.Warning);

// The component factory appends invocation-specific providers and applies this module once.
services.ConfigureFlamme(flamme => flamme
    .UseWorkflows(workflows => workflows.UseCommitStrategies(strategies =>
    {
        strategies.AddStandardStrategies();
        strategies.Add("Every 10 seconds", new PeriodicWorkflowStrategy(TimeSpan.FromSeconds(10)));
    }))
    .UseFlowchart(flowchart => flowchart.UseTokenBasedExecution())
    .UseWorkflowManagement(management =>
    {
        management.SetDefaultLogPersistenceMode(LogPersistenceMode.Inherit);
        management.UseCache();
        management.UseReadOnlyMode(false);
    })
    .UseWorkflowRuntime(runtime =>
    {
        runtime.UseCache();
        runtime.UseDistributedRuntime();
    })
    .UseWorkflowsApi()
    .UseScheduling()
    .UseHttp(http => http.UseCache()));

services.AddActivityStateFilter<ComponentHttpRequestAuthenticationHeaderFilter>();
AddTestControllers(services);

var app = builder.Build();

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseTenants();
app.UseJsonSerializationErrorHandler();
app.UseWorkflows();
app.MapWorkflowsApi("elsa/api");
app.MapControllers();

await app.RunAsync();

[System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026",
    Justification = "This JIT-run component host intentionally exercises the production MVC controller endpoints.")]
static void AddTestControllers(IServiceCollection services) => services.AddControllers();
