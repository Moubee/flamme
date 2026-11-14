using Elsa.Workflows.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Elsa.ServiceDefaults;

/// <summary>Configures host and workflow telemetry for local development and OTLP collectors.</summary>
public static class Extensions
{
    /// <summary>
    /// Adds logs, metrics, and traces while retaining the host's existing health checks and HTTP configuration.
    /// Standard OTEL environment variables configure the collector, authentication, and resource identity.
    /// </summary>
    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddMeter(WorkflowInstrumentation.MeterName))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation(options =>
                {
                    // Aspire polls these endpoints frequently; retain workflow and management requests.
                    options.Filter = context => !context.Request.Path.StartsWithSegments("/health") && context.Request.Path != "/";
                })
                .AddHttpClientInstrumentation()
                .AddSource(WorkflowInstrumentation.ActivitySourceName));

        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        return builder;
    }
}
