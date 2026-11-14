using CShells.AspNetCore.Configuration;
using CShells.AspNetCore.Extensions;
using CShells.DependencyInjection;
using Elsa.Dashboard.Api.ShellFeatures;
using Elsa.ExternalAuthentication.OpenIdConnect.ShellFeatures;
using Elsa.ExternalAuthentication.Secrets.ShellFeatures;
using Elsa.UserTasks.Persistence.EFCore.PostgreSql.ShellFeatures;
using Elsa.UserTasks.ShellFeatures;
using Elsa.ModularServer.Web;
using Elsa.ModularServer.Web.Catalog;
using Elsa.Platform.Integration.ShellFeatures;
using Elsa.ShellFeatures;
using Elsa.Workflows.Api.ShellFeatures;
using Elsa.Workflows.Management.ShellFeatures;
using Elsa.Workflows.Runtime.Dashboard.ShellFeatures;
using Elsa.Workflows.Runtime.Distributed.ShellFeatures;
using Elsa.Workflows.Runtime.ShellFeatures;
using Elsa.Workflows.ShellFeatures;
using Elsa.Workflows.Telemetry;
using Nuplane;
using Nuplane.Loading.Hosting.Builder;
using Nuplane.Sources.Directory.Configuration;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);
var services = builder.Services;
var configuration = builder.Configuration;
configuration.AddJsonFile(configuration["Elsa:PlatformIntegration:ShellOverlayPath"] ?? "platform-shell-overrides.json", optional: true, reloadOnChange: false);

builder.Logging.AddOpenTelemetry(logging =>
{
    logging.IncludeFormattedMessage = true;
    logging.IncludeScopes = true;
    logging.ParseStateValues = true;
});

var telemetry = services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddSource(WorkflowInstrumentation.ActivitySourceName))
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddMeter(WorkflowInstrumentation.MeterName));

if (!string.IsNullOrWhiteSpace(configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]) ||
    !string.IsNullOrWhiteSpace(configuration["OTEL_EXPORTER_OTLP_TRACES_ENDPOINT"]) ||
    !string.IsNullOrWhiteSpace(configuration["OTEL_EXPORTER_OTLP_METRICS_ENDPOINT"]) ||
    !string.IsNullOrWhiteSpace(configuration["OTEL_EXPORTER_OTLP_LOGS_ENDPOINT"]))
{
    telemetry.UseOtlpExporter();
}

var nuplaneConfiguration = configuration.GetSection("Nuplane");

services.AddNuplane(nuplaneConfiguration, nuplane =>
{
    nuplane.AddDirectoryFeedsFromConfiguration(nuplaneConfiguration);
    nuplane.AutoloadPackages(nuplaneConfiguration.GetSection("Loading"));
    nuplane.OnPackagesChanged<MyPackageObserver>();
});

services.AddSingleton<NuplaneAssemblyProvider>();

builder.AddShells(shells => shells
    .WithHostAssemblies()
    .WithAssemblyProvider<NuplaneAssemblyProvider>()
    .WithConfigurationProvider(configuration)
    .WithWebRouting(options => options.EnablePathRouting = true)
    .WithAuthenticationAndAuthorization()
    .ConfigureAllShells(shell =>
    {
        shell.WithFeatures(
            typeof(ElsaFeature),
            typeof(WorkflowManagementFeature),
            typeof(WorkflowRuntimeFeature),
            typeof(WorkflowsFeature),
            typeof(DistributedRuntimeFeature),
            typeof(ElsaPlatformIntegrationFeature),
            typeof(OpenIdConnectExternalAuthenticationFeature),
            typeof(ElsaSecretsExternalAuthenticationFeature),
            typeof(DashboardApiFeature),
            typeof(WorkflowRuntimeDashboardFeature),
            typeof(WorkflowsApiFeature),
            typeof(UserTasksFeature),
            typeof(PostgreSqlUserTasksPersistenceShellFeature));
    }));

services.AddSingleton<PluginCatalog>();
services.AddHealthChecks();

services.AddAuthentication();
services.AddAuthorization();

var app = builder.Build();

app.MapHealthChecks("/");
app.MapShells();
app.UseAuthentication();
app.UseAuthorization();
app.MapSampleCatalog();
app.Run();
