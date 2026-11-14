# Telemetry ownership

Flamme emits workflow traces and metrics through `System.Diagnostics.ActivitySource`
and `System.Diagnostics.Metrics.Meter`. Host applications configure the official
OpenTelemetry SDK, instrumentation, processors, and exporters. Collectors and
observability backends own telemetry ingestion, retention, querying, and visualization.
TUnit owns test-run logging and telemetry.

## Research and scope

- `WorkflowInstrumentation` in `Elsa.Workflows.Core` already emits workflow and activity
  spans, lifecycle counters, and activity duration measurements without an SDK dependency.
  Preserve this instrumentation, HTTP trace propagation, and their behavioral tests.
- `Elsa.Diagnostics.OpenTelemetry` implements a collector: OTLP ingestion, in-memory
  storage, query endpoints, source registration, redaction, and SignalR delivery.
  Remove the whole package and its dedicated tests.
- `Elsa.Diagnostics.ConsoleLogs`, `Elsa.Diagnostics.StructuredLogs`, their dashboard
  packages, and `Elsa.Diagnostics.StructuredLogs.Persistence.Relational` implement
  custom log capture, storage, querying, and streaming. Remove these packages, their
  dedicated tests, application registrations, and dashboard contracts.
- The modular server uses the official OTLP exporter but adds a custom
  `Diagnostics:OpenTelemetry:Exporter` configuration layer and exports back into its
  own `/elsa/otlp` route. Replace this with the official exporter's standard `OTEL_*`
  configuration for an external backend.
- Shared test applications use a custom `TextWriterLoggerProvider`. Replace that
  plumbing with TUnit logging; retain focused listeners or logger fakes that verify
  instrumentation and logging behavior.
- Workflow execution records, activity execution logs, retry history, and ordinary
  `ILogger` calls are workflow functionality and remain in scope as producers.
- The base branch has no Aspire AppHost or ServiceDefaults project. Do not import
  the separate Aspire development branch or scaffold unrelated orchestration.

This is an intentional breaking change. There are no compatibility facades for the
removed packages, APIs, shell features, dashboard log sections, or configuration keys.

## Delivery steps

1. Record the ownership boundary, removal scope, and verification criteria.
2. Remove custom diagnostics packages and dedicated tests, then remove their
   solution entries, project references, application consumers, and dashboard surfaces.
3. Delegate shared test logging to TUnit and preserve instrumentation assertions.
4. Update host exporter configuration, migration documentation, and dependency locks.

Commit each step with a conventional commit message. Review the combined changes after
these four steps, fix findings in follow-up commits, and repeat the relevant checks.

## Verification criteria

- No production Elsa diagnostics collector, telemetry/log storage, custom exporter,
  log capture provider, ingestion/query endpoint, or live telemetry hub remains.
- No active project, solution, host configuration, or dashboard contract references a
  removed diagnostics package or feature.
- Host export uses official OpenTelemetry packages and standard OTLP configuration;
  no default endpoint points back into Flamme.
- Workflow and HTTP instrumentation tests continue to verify emitted spans, metrics,
  and trace propagation. Test-run output uses TUnit's logging support.
- Restore the complete solution with all target frameworks and review lock changes.
- Build the solution and run relevant tests for .NET 10 and .NET 11, followed by the
  broader suite required by `AGENTS.md` for shared infrastructure changes.
- Review the diff for broken project references, unintended domain behavior changes,
  stale user-facing instructions, and dependency or test-run regressions.

The initial environment provides .NET SDK 11.0.100, .NET 10/11 runtimes, and Aspire CLI
13.6.5. Keep framework and package versions centralized and avoid unrelated upgrades.
