# Diagnostics Console Logs migration

`Elsa.Diagnostics.ConsoleLogs` and its dashboard adapter have been removed. Elsa no longer hooks process stdout/stderr, maintains recent console buffers, or exposes console REST/SignalR streams.

Use the process or container logging pipeline for stdout/stderr and your observability platform for storage and viewing. TUnit owns capture and correlation for test-run logs and telemetry.

Remove console-log package references, feature registrations, console capture hooks, hub mappings, and configuration. See [OpenTelemetry workflow instrumentation and the diagnostics migration](opentelemetry-workflows.md#upgrading-from-elsa-diagnostics-packages) for the complete removal scope and host configuration.
