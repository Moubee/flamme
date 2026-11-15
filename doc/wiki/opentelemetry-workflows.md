# OpenTelemetry Workflow Instrumentation

Elsa emits workflow and activity traces and metrics through `System.Diagnostics`. The runtime provides the `Elsa.Workflows` activity source and meter; the host application selects the OpenTelemetry SDK, exporters, collector, and observability backend.

## Host configuration

Configure the official OpenTelemetry packages in the application host. No Elsa diagnostics package is required:

```csharp
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddSource("Elsa.Workflows"))
    .WithMetrics(metrics => metrics.AddMeter("Elsa.Workflows"))
    .UseOtlpExporter();
```

`UseOtlpExporter` is supplied by `OpenTelemetry.Exporter.OpenTelemetryProtocol` with `OpenTelemetry.Extensions.Hosting`. It enables the official exporter for logs, metrics, and traces. Add standard ASP.NET Core and HTTP client instrumentation in the host as needed. Keep operational logging on `Microsoft.Extensions.Logging`.

Configure an external collector or backend using standard environment variables:

```bash
OTEL_SERVICE_NAME=elsa-server
OTEL_RESOURCE_ATTRIBUTES=service.instance.id=local-dev
OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4318
OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf
```

The SDK handles protocol options, signal-specific paths, headers, batching, and export. For signal-specific endpoints and other options, see the [official OTLP exporter documentation](https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/src/OpenTelemetry.Exporter.OpenTelemetryProtocol/README.md).

The reference server registers the workflow source and meter through
[`Elsa.ServiceDefaults`](../../src/apps/Elsa.ServiceDefaults/Extensions.cs). It enables
OTLP export when `OTEL_EXPORTER_OTLP_ENDPOINT` is configured. The
[Aspire AppHost](../aspire-development.md) supplies that endpoint for local
development; standalone hosts must configure their collector endpoint explicitly.

## Traces

Elsa creates spans around workflow execution cycles and activity execution. The spans include workflow and activity identifiers, definition metadata, status, tenant ID when available, and fault status. Workflow input, activity input, output payloads, headers, and variable values are not added as span attributes.

Faulted workflow and activity spans use `ActivityStatusCode.Error` and record the exception type as `exception.type` when an exception is available. Exception messages and stack traces are not added to spans or exception events by Elsa workflow instrumentation.

Outbound `SendHttpRequest` and `FlowSendHttpRequest` calls inject the current W3C trace context headers when an active workflow/activity span exists, so downstream services can continue the same trace. Enable standard .NET HTTP client instrumentation in the host when you want outbound HTTP spans for these calls.

## Metrics

The `Elsa.Workflows` meter emits:

- `elsa.workflow.started`
- `elsa.workflow.completed`
- `elsa.workflow.faulted`
- `elsa.activity.duration` in seconds

Metric tags use low-cardinality workflow and activity metadata and omit per-execution identifiers. The started counter omits execution status tags because it records the pre-execution boundary; completed and faulted counters include terminal workflow status tags.

## Upgrading from Elsa diagnostics packages

The following custom telemetry packages and their tests have been removed:

- `Elsa.Diagnostics.OpenTelemetry`
- `Elsa.Diagnostics.StructuredLogs`
- `Elsa.Diagnostics.StructuredLogs.Persistence.Relational`
- `Elsa.Diagnostics.StructuredLogs.Dashboard`
- `Elsa.Diagnostics.ConsoleLogs`
- `Elsa.Diagnostics.ConsoleLogs.Dashboard`

Remove package references and feature registration for `OpenTelemetry`, `StructuredLogs`, `ConsoleLogs`, `StructuredLogsDashboard`, and `ConsoleLogsDashboard`. Remove their `Use*`/`Add*` calls, hub mappings, console capture hooks, and related configuration.

Elsa no longer hosts OTLP ingestion, telemetry buffers, log/trace/metric query endpoints, collector configuration APIs, or telemetry SignalR hubs. Existing diagnostics clients and dashboard integrations must use your observability platform instead. Retention, storage, queries, streaming, redaction, and access policies belong to that platform and the host's logging/export pipeline.

Replace `Diagnostics:OpenTelemetry:Exporter:Endpoint` and `Diagnostics:OpenTelemetry:Exporter:Protocol` with `OTEL_EXPORTER_OTLP_ENDPOINT` and `OTEL_EXPORTER_OTLP_PROTOCOL`. Point the endpoint at an external collector or backend; the former `/elsa/otlp/v1/*` routes are gone. For HTTP/protobuf, supply a collector base URL and let the official SDK add the signal paths.

Replace any legacy `Elsa.OpenTelemetry` extension registration with the built-in workflow source/meter and host-owned SDK configuration. Do not add duplicate workflow tracing middleware.

Workflow execution journals, persisted workflow/activity records, and retry history remain workflow domain data. This removal concerns operational telemetry capture and storage.

TUnit handles test-run logs and telemetry. Test hosts use TUnit's logging and ASP.NET Core integration rather than Elsa telemetry exporters or log-capture providers. Focused instrumentation tests can still observe the public .NET source/meter to verify emitted spans and measurements.
