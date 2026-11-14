# Diagnostics Structured Logs migration

`Elsa.Diagnostics.StructuredLogs`, its relational persistence helpers, and its dashboard adapter have been removed. Elsa no longer buffers, stores, queries, or streams operational log records.

Keep application logging on `Microsoft.Extensions.Logging` and configure official OpenTelemetry exporters or another host-owned logging provider. Your observability backend owns log storage, retention, queries, redaction, and access control.

Remove structured-log feature registrations, package references, hub mappings, and configuration. See [OpenTelemetry workflow instrumentation and the diagnostics migration](opentelemetry-workflows.md#upgrading-from-elsa-diagnostics-packages) for the complete removal scope and host configuration.
