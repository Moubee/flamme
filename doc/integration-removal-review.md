# Integration removal review

## Checkpoint after three commits

Reviewed on 2026-11-14 after:

1. `6823deab0` — scope and acceptance plan.
2. `0e310a77f` — BPMN, WebhooksCore and Platform recipe retirement.
3. `7b6cc6473` — modular host and shell API/client retirement.

Two implementation agents independently reviewed code outside their original
scope. The coordinator checked the source and integrated the findings below.
This checkpoint precedes completed shell removal and build/restore validation.

| Priority | Finding | Resolution |
| --- | --- | --- |
| P1 | The solution and central package declarations still listed the retired Webhooks module. | Removed its solution entry and WebhooksCore declaration during graph integration. |
| P2 | Migrating a shell named `Default` to classic hosting changes the persisted pause key to lowercase `default`; startup could otherwise dispatch work despite the old administrative pause. | Added a stopped-runtime key migration and pause-recovery gate to the migration guide. Automatic merging of multiple shell pause states is deliberately avoided. |
| P2 | Active BPMN/wiki instructions still advertised removed routes and source paths. | Replace current instructions with a retirement/migration notice; preserve historical specifications and ADRs. |
| P3 | Permission-guide tests previously received retained module descriptors transitively through the removed modular host. | Added explicit retained module references to keep catalog completeness coverage. |

Static review confirmed that all 340 retained project references resolved at this
checkpoint and that 26 inspected declarations for the exception groups retained
their baseline versions. Script, AI, blob, generic HTTP, RunTask and classic
runtime drain/pause implementations remain. Package counts and deletion counts
are not performance measurements.

Automatic approval review initially interpreted shell adapter deletion as
removing retained capabilities. The user explicitly approved deleting CShells-only
adapters while preserving their classic implementations and dependencies. That
approved removal is now committed; the initial rejection no longer blocks work.

## Checkpoint after the next three commits

Reviewed on 2026-11-15 after:

1. `101b7959e` — active documentation and static-hosting ADR.
2. `ee5b12b6e` — approved CShells adapter removal and classic-path test migration.
3. `01e4dd222` — package/build/solution cleanup and regenerated locks.

Independent reviewers rechecked retained feature registrations, test migrations,
project references and lock deltas. No unresolved actionable finding remains.
The final fixes remove the now-unused `SharedPersistenceSettings` DTO (its only
consumers were the deleted shell persistence bases), migrate three module README
examples, and mark historical technology annotations clearly. Generic service
registration extensions and `ShortGuidIdentityGenerator` remain supported.

All 26 inspected exception-group declarations retain their baseline versions.
All 340 project references, the solution, and both solution filters resolve.
Across 47 changed surviving locks, no package keys were added and content hashes
at unchanged versions are stable. Nine transitive package IDs resolve lower after
removing paths that imposed higher minimum versions: DependencyModel
10.0.1 to 10.0.0 in five target entries, Http 10.0.15 to 10.0.13 in one host-smoke
target, and the seven IdentityModel-family packages 8.23.0 to 8.19.2 in the
host-smoke project only. These are graph-pruning effects with central transitive
pinning disabled, not changes to retained direct dependency versions.

## Verification evidence

Toolchain: .NET SDK **11.0.100**, MSBuild **18.12.2**, Aspire CLI **13.6.5**.
The baseline has no Aspire AppHost, so there is no AppHost lifecycle to operate.

| Check | Result |
| --- | --- |
| Full-target `dotnet restore Elsa.slnx --force-evaluate --packages <empty-temporary-cache> --no-http-cache --disable-build-servers` | Passed using nuget.org only, without Feedz. |
| `dotnet restore Elsa.slnx --locked-mode --packages <same-cache> --disable-build-servers` | Passed after lock regeneration. |
| Standalone `test/TlsSmoke/TlsSmoke.csproj` full-target restore and Release build | Passed on .NET 10 and .NET 11, with zero warnings/errors. |
| `dotnet build Elsa.slnx --configuration Release --no-restore --disable-build-servers -m:4 -p:UseSharedCompilation=false` | Passed: **189 warnings, 0 errors**. |
| `dotnet test --solution Elsa.slnx --configuration Release --no-build --no-restore --max-parallel-test-modules 4` | Passed: **8,379 succeeded, 4 skipped, 0 failed**. .NET 10: 4,187 passed / 2 skipped; .NET 11: 4,192 passed / 2 skipped. Includes PostgreSQL-backed persistence/component coverage and both classic-host smoke suites. |
| `dotnet package list --project Elsa.slnx --include-transitive --format json --no-restore` | All **109 projects / 217 target graphs** inspected: no retired package families; exception-group packages remain. |
| Restored assets and committed locks | **110 asset graphs** including standalone TLS and **50 locks** inspected; zero retired package entries. Assets list only nuget.org as their restore source. |
| `dotnet nuget why` on the retained server | Confirms no CShells dependency on net11.0 and no Bpmn.Model dependency on net10.0. |
| Static server publish for net10.0 and net11.0 | Passed; **120 / 121** published dependency entries inspected, with no retired libraries or binaries. |
| Release solution `dotnet pack --no-build --no-restore --output <temporary-directory> -p:Version=3.9.0-preview.integration-removal` | Passed; **60 nupkgs** inspected: no retired dependencies or generated `elsa-package.json` catalog manifests. |
| ADR index, local documentation links and `git diff --check` | Passed. |

The four skips are the two existing component cases on both targets:
`ImportWorkflowActivity_ShouldUpdateOtherPods` (not implemented) and
`DeleteWorkflow_Clustered` (existing interference with event-driven tests).
No failing test was suppressed or converted to a skip.

The retained reference-server lock graph shrank from **135 / 136** to
**124 / 125** entries on net10.0 / net11.0. These counts include local project
entries and are dependency inventory evidence, not performance measurements.

External persisted BPMN/workflow/plugin data and deployment-specific shell
configuration still require the migration described in the guide; this change
does not automatically convert deployed state. No throughput benchmark or live
production deployment was performed.
