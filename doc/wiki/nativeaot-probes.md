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

The Features probe additionally accepts `--provider explicit-catalog`. It compiles
an independent catalog caller covering dependency graphs, policies, runtime
configuration, metadata isolation, typed hosted-service identities, and deterministic negative cases. The default
`legacy-baseline` keeps the original reflection-based observation. Provider
selection is a compiler input, so the explicit executable does not root the
legacy bootstrap. Both providers use the same module lifecycle and explicitly
select their admission policy; see [module composition](module-system.md#explicit-feature-composition).

Each executable also runs an independent asynchronous/generated-JSON control.
JSON reflection is disabled in managed and native publication. The default baseline
fixtures retain managed discovery; the explicit catalog is a runtime oracle. Future generated providers need
separate parity and native acceptance evidence.

## Run a baseline

Use the SDK selected by [global.json](../../global.json), both supported runtimes,
and Bash. Native publication also needs the target OS compiler and development
libraries; see Microsoft's [Native AOT prerequisites](https://learn.microsoft.com/dotnet/core/deploying/native-aot/).
The wrapper publishes and executes on the current host, so its RID must match the
host OS and architecture. Linux evidence must be produced on Linux.

```bash
scripts/nativeaot/run.sh --probe features --mode managed --framework net10.0
scripts/nativeaot/run.sh --probe features --provider explicit-catalog --mode native --framework net11.0
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
artifacts/nativeaot/<legacy-baseline|explicit-catalog>/<probe>/<mode>/<rid>/<default|disabled>/
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

## Compiled module exports and packages

The explicit Features caller also consumes a separately compiled producer and a
host-authored export, validating versioned dependencies before normal feature
activation. For a fresh consumer that installs actual runtime/producer packages:

```bash
scripts/nativeaot/test-module-packages.sh
```

This independent matrix checks package versions/hashes, locked two-framework
restores, native execution, host/scoped isolation, and failure controls. See the
[export guide](nativeaot-module-exports.md#validate-producerconsumer-boundaries)
for its package-only boundary, evidence locations, and public API contract.

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
Changing provider does not change dependencies and reuses the same lock variant.
Source diagnostics from the legacy reflection provider remain visible even when
its methods are unreachable from the explicit native caller. The all-source
`--check` gate therefore remains stricter than a successful component execution;
inspect source and native-compiler diagnostics separately.

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

The explicit Features caller also resolves real singleton hosted services, starts
them in registration order, stops them in reverse, and verifies owned scope
disposal and rejection of captive scoped constructor dependencies. It is a
component lifecycle fixture, not a production Generic Host or Elsa worker.

The probes are diagnostic tools and are not required green NativeAOT CI gates.
Generated exports, production metadata/provider fixes, broader workflow fixtures,
generated packed-consumer parity, and Linux acceptance gates remain subsequent migration
work. Store run logs and review records as CI/PR artifacts rather than committing
task transcripts.
