using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Elsa.ServiceDefaults;
using Elsa.Workflows.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Elsa.Hosts.SmokeTests;

public class ServiceDefaultsTests
{
    [Test]
    public async Task WorkflowSpansAndMeasurementsReachExportersWithoutAnOtlpCollector()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] = "";
        builder.AddServiceDefaults();

        var spanExporter = new SpanExporter();
        var metricExporter = new MetricExporter();
        builder.Services.AddOpenTelemetry()
            .WithTracing(tracing => tracing.AddProcessor(new SimpleActivityExportProcessor(spanExporter)))
            .WithMetrics(metrics => metrics.AddReader(new PeriodicExportingMetricReader(metricExporter)));

        using var host = builder.Build();
        await host.StartAsync();

        using var source = new ActivitySource(WorkflowInstrumentation.ActivitySourceName);
        using (var activity = source.StartActivity("aspire.workflow.export-test"))
        {
            await Assert.That(activity).IsNotNull();
        }

        using var meter = new Meter(WorkflowInstrumentation.MeterName);
        var counterName = $"elsa.aspire.export_test.{Guid.NewGuid():N}";
        var counter = meter.CreateCounter<long>(counterName);
        counter.Add(7);
        var meterProvider = host.Services.GetRequiredService<MeterProvider>();
        await Assert.That(meterProvider.ForceFlush()).IsTrue();

        await Assert.That(spanExporter.OperationNames.Contains("aspire.workflow.export-test")).IsTrue();
        await Assert.That(metricExporter.Sums.TryGetValue(counterName, out var sum)).IsTrue();
        await Assert.That(sum).IsEqualTo(7);

        await host.StopAsync();
    }

    private sealed class SpanExporter : BaseExporter<Activity>
    {
        public ConcurrentBag<string> OperationNames { get; } = [];

        public override ExportResult Export(in Batch<Activity> batch)
        {
            foreach (var activity in batch)
            {
                OperationNames.Add(activity.OperationName);
            }

            return ExportResult.Success;
        }
    }

    private sealed class MetricExporter : BaseExporter<Metric>
    {
        public ConcurrentDictionary<string, long> Sums { get; } = new();

        public override ExportResult Export(in Batch<Metric> batch)
        {
            foreach (var metric in batch)
            {
                if (!metric.Name.StartsWith("elsa.aspire.export_test.", StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (ref readonly var point in metric.GetMetricPoints())
                {
                    Sums[metric.Name] = point.GetSumLong();
                }
            }

            return ExportResult.Success;
        }
    }
}
