# NativeAOT diagnostic probes

The four console probes establish a reproducible baseline for the first NativeAOT
migration step. They call existing production code and keep unresolved failures
visible. Their `legacy-baseline` provider does not implement generated composition
or establish NativeAOT support for the libraries, server, persistence, or scripting.

| Probe | Production path |
| --- | --- |
| `features` | `Elsa.Features` default activation, declared dependency ordering, lifecycle, and installed-feature registry. No Workflow Core reference. |
| `execution` | Explicit factories for the stock core features, the real workflow runner, and a sequence containing an awaited activity. |
| `payloads` | Standalone `JsonPayloadSerializer`, registered DTO/list aliases, nested bodies, and scalar contracts. An authored System.Text.Json context supplies an independent positive control. |
| `integrated` | Default `WorkflowsFeature` activation, the real workflow runner, and production payload serialization. |

Each executable also runs an independent asynchronous/generated-JSON control.
JSON reflection is disabled in managed and native publication. These baseline
fixtures retain the current production provider; future generated providers need
separate parity and native acceptance evidence.

## Run a baseline

Use the SDK selected by [global.json](../../global.json), both supported runtimes,
and Bash. Native publication also needs the target OS compiler and development
libraries; see Microsoft's [Native AOT prerequisites](https://learn.microsoft.com/dotnet/core/deploying/native-aot/).
The wrapper publishes and executes on the current host, so its RID must match the
host OS and architecture. Linux evidence must be produced on Linux.

```bash
scripts/nativeaot/run.sh --probe features --mode managed --framework net10.0
scripts/nativeaot/run.sh --probe features --mode native --framework net11.0
scripts/nativeaot/run.sh --probe execution --mode native --framework net11.0 --runtime-async disabled
scripts/nativeaot/run.sh --probe payloads --mode native --framework net11.0
```

Supported framework inputs are `net10.0` and `net11.0`. Keep the default .NET 11
runtime-async lane as well as the rebuilt opt-out. ConfigureAwait.Fody remains
active in the probes and their production references. Native execution checks
that dynamic code is unavailable. A managed binary cannot satisfy a native check.

Every run returns the actual failing restore, evaluation, publication, or probe
exit code. Expected baseline failures remain nonzero. Do not invert them into a
passing support check. `--check` additionally treats linker/compiler IL warnings
as errors and rejects source IL diagnostics; it is intended for capabilities as
they migrate. It does not mark generated coverage complete.

Evidence is written beneath:

```text
artifacts/nativeaot/legacy-baseline/<probe>/<mode>/<rid>/<default|disabled>/
  build/                 # isolated restore/build intermediates, per target
  publish/<framework>/   # executable and dependencies
  evidence/<framework>/  # inputs, SDK/toolchain, evaluated properties,
                         # restore/publish logs, binlog, IL diagnostics,
                         # execution output, result.json, exit-code.txt
```

The current invocation replaces that target's evidence and publication output.
Archive evidence before rerunning a lane. The raw IL inventory preserves member
messages and original logs; triage diagnostic identity, assembly/member, framework,
and dependency version rather than comparing warning totals. Diagnostic runs
allow unresolved warnings so the native process can reveal runtime failures.
Every publication run rebuilds sources and native outputs so incremental
compilation cannot hide diagnostics. A zero process exit also requires the
ordered harness-control, selected production scenario, runtime identity, and
final provider/mode/scope markers; missing or inconsistent results fail validation.

## Dependency locks and maintenance

Ordinary solution builds default to managed mode and use each executable's
`packages.lock.json`. Wrapper runs select that executable's
`locks/<mode>/<rid>/packages.lock.json`. Every lock includes both frameworks.
Committed variants cover `managed`/`native` on `osx-arm64` and `linux-x64`.
Other accepted RIDs require explicit lock generation and review first.

```bash
scripts/nativeaot/run.sh --probe features --mode native --rid linux-x64 --update-lock
```

`--update-lock` restores both frameworks without locked mode and stops before
publication. It can prepare another OS's lock without a native compiler. Run it
for each affected probe/mode/RID after dependency changes, review the locks, then
use normal locked runs. Restore and publication receive identical graph settings;
the lock path is local to the entry point and never imposed on its references.
No broad compatibility declarations, roots, or warning suppressions are added.

## Check the harness

```bash
scripts/nativeaot/test-wrapper.sh
dotnet run --project src/apps/Elsa.NativeAot.Features.Smoke -c Release -f net11.0 -- --control-only
dotnet run --project src/apps/Elsa.NativeAot.Features.Smoke -c Release -f net11.0 -- --self-test-failure
dotnet run --project src/apps/Elsa.NativeAot.Features.Smoke -c Release -f net11.0 -- --expect-mode native
```

The wrapper checks use a fake CLI to inject restore, publication, evaluation,
execution, compiler, and diagnostic failures; they also test lock maintenance,
argument validation, stale output, and runtime-async propagation. The last two
commands above must fail. `--control-only` proves only the shared harness and
generated JSON control, never the production scenario. Run these options against
the published native executable too when checking the native harness.

The probes are diagnostic tools and are not required green NativeAOT CI gates.
Generated exports, production metadata/provider fixes, broader workflow fixtures,
packed-consumer parity, and Linux acceptance gates remain subsequent migration
work. Store run logs and review records as CI/PR artifacts rather than committing
task transcripts.
