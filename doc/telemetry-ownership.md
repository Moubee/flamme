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

## Review checkpoint

The four planned steps were committed as `c672acb5d`, `4eaf060fb`, `4e59f5d8f`, and
`8ab7c9e27`. Independent review started after the third commit and checked the final
combined change after the fourth.

Review identified that the modular host enabled the official exporter only for the
generic OTLP endpoint. Commit `c650fdf1f` also accepts the standard traces, metrics,
and logs endpoint variables, leaving their interpretation to the official package.

The first complete test run detected two failures, one per framework, because the
authorization migration table still recommended removed diagnostics permissions. The
guide now explicitly marks those permissions removed, and all 12 host smoke tests
pass on rerun. No test was weakened or excluded to address these failures.

Final review found no remaining issues. Native instrumentation assertion listeners and
the focused warning-assertion logger remain test helpers; production workflow
instrumentation, trace propagation, execution journals, and retry history are unchanged.

## Validation results

- Full-target `dotnet restore Elsa.slnx --force-evaluate` passed. The standalone TLS
  smoke project was restored separately. Both also passed `--locked-mode` restore.
- Reviewed every changed retained lock file: no resolved package version changed;
  changes remove diagnostics dependencies and add or centrally pin TUnit logging.
- `dotnet build Elsa.slnx --no-restore` passed for both frameworks with zero errors
  and 189 warnings. The host was rebuilt successfully after the reviewed endpoint fix.
- Workflow core tests passed: 624 tests across .NET 10 and .NET 11.
- Final `dotnet test --solution Elsa.slnx --no-build --no-restore
  --max-parallel-test-modules 2` passed across both frameworks: 8,883 succeeded,
  zero failed, and four existing component-test skips (two skipped scenarios per
  framework). This includes Docker-backed PostgreSQL, host, and component tests.
- All project-reference paths resolve, whitespace checks pass, and retained source
  and lock files contain no references to the removed diagnostics packages.
- The original checkout's unrelated local changes were preserved.

## TUnit logging follow-up

The local TUnit reference was inspected read-only at commit
`77453eab9cc452dbba8c53e77d12bf595f71cf30`, which matches the installed TUnit 1.84.0
NuGet package source. Its official documentation distinguishes automatic logging for
`TestWebApplicationFactory` from standalone `IHost` and plain dependency-injection
containers. `TestApplicationBuilder` constructs the latter, so it retains a direct
`TUnit.Logging.Microsoft` dependency and one official provider registration. Native
console interception and activity collection do not install an `ILogger` provider
into an independent container.

This check found that setting the provider minimum to Debug alone had lost the old
global Debug filter. A regression test confirmed Debug messages were absent on both
frameworks while Information messages were captured. The builder now uses the
official `AddLogging`/`AddCorrelatedTUnit` extensions with both minimum levels set to
Debug. All 656 workflow integration tests pass, including actual Debug and Information
capture assertions using TUnit's own output API. Independent review found no remaining
issues in this correction.

The full solution rebuild and test run also passed:
`dotnet test --solution Elsa.slnx --no-restore --max-parallel-test-modules 2`
completed with 8,887 passed, zero failed, and four existing skips across .NET 10 and
.NET 11. No dependency or lock-file changes were needed.

## CI scheduling failure

The Linux .NET 11 Release [CI job](https://github.com/DenDeline/flamme/actions/runs/43635055396/job/132569977163)
aborted with exit code 134 because a cron callback read
`CancellationTokenSource.Token` after another thread disposed the source. The read
was outside the callback's exception boundary, so the exception escaped the
asynchronous timer event and terminated the test process. The .NET 10 job and all
four Docker smoke checks passed on that revision.

Cron, recurring, and specific-instant tasks now capture their token during construction
and route their callbacks through an awaitable method with a complete exception
boundary. Scope creation, command resolution, execution, and teardown are covered.
The existing timer scheduling and deferred-cancellation behavior are preserved.

Nine deterministic cases per framework cover disposal during scope creation, queued
callbacks after disposal, and disposal while a command is executing. Using the original
cron callback logic in the awaitable method reproduces the exact disposed-token
exception on both frameworks; the production fix is restored after this check.

All 142 scheduling tests pass in Release mode across .NET 10 and .NET 11, including
the new disposal cases. No dependencies, runtime-async settings, or CI test selections
were changed to address the failure.
