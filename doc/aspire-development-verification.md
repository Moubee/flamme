# Aspire verification and review

Verified on 2026-11-14 in the separate `aspire-development` worktree, branch
`feat/aspire-development`, created from freshly fetched `origin/main` at
`8c50b8669`. Other worktrees were not edited. SDK: .NET 11.0.100. Aspire CLI,
AppHost SDK and PostgreSQL hosting integration: 13.6.5.

The build, test, and runtime results below describe the original AppHost layout
and host inventory before the later relocation and upstream removals. The
follow-up section records validation for that later revision separately.

## Build and tests

| Check | Result |
| --- | --- |
| `dotnet restore Elsa.slnx --force-evaluate` | Passed; only affected executable lock files changed. |
| `dotnet restore Elsa.slnx --locked-mode` | Passed with committed locks. |
| `dotnet build Elsa.slnx --no-restore -m:1` | Passed for both supported frameworks; 159 warnings in existing code, zero errors. |
| `dotnet test --project test/integration/Elsa.Hosts.SmokeTests/Elsa.Hosts.SmokeTests.csproj --no-build --no-restore` | 14 passed, zero failed/skipped: seven on each framework. |
| `git diff --check` | Passed. |

The initial parallel solution build was cancelled while dependency setup was
taking time. The serial solution build completed, including the existing Copilot
CLI download. The complete solution test suite was not run; the relevant real-host
smoke suite covered both reference hosts at that revision.

## Runtime demonstration

- Started the exact AppHost with `aspire start --non-interactive --isolated`.
- `aspire wait postgres` and `aspire wait server` both reported healthy.
- Confirmed the server process uses `dotnet run ... --no-launch-profile --framework net11.0`.
- Confirmed dynamic connection injection for the root and all three sample tenants.
- HTTP and HTTPS `/health/live` and `/health/ready` returned `Healthy`.
- HTTP and HTTPS `/workflows/hello-world-dsl` returned
  `Message: Hello World from DSL via Expressions!`, including after restart.
- Aspire telemetry contained request spans with HTTP 200, nested `workflow.execute`
  and `activity.execute` spans, completed workflow status and no workflow faults.
  Structured startup logs were also exported. The dashboard metric UI was not
  manually inspected.
- Development `admin` / `password` login returned authenticated status and a token;
  an authenticated `/elsa/api/workflow-definitions` request returned 200. Tokens
  and generated resource credentials are not included in this report.
- Wrote a temporary database marker, stopped/started the exact AppHost normally,
  and verified the marker and container identity were retained. Removed only the
  temporary validation table afterward. The PostgreSQL 17 data volume mounts at
  `/var/lib/postgresql/data` and has an AppHost-path-specific generated name.
- `aspire resource server rebuild` succeeded and the server became healthy again.
- Validation ends with ordinary exact-target AppHost shutdown. Persistent
  PostgreSQL resources and data are retained; no force/volume deletion was used.

## Independent review after four steps

A separate reviewer examined the complete branch, resource graph, SDK boundaries,
tenant settings, persistence, telemetry, lock files and developer instructions.

1. **Fixed:** multi-targeted server launch lacked a framework argument. Runtime
   logs reproduced the failure; explicit `--framework net11.0` fixed startup.
2. **Fixed:** interactive and background examples used different isolation modes.
   Both interactive and background examples now use `--isolated`, matching the
   verified development workflow.

The reviewer found no additional actionable code defects. Upstream `origin/main`
advanced independently during the task; initial review used the original merge
base. When publishing the pull request, current `main` was merged into the branch.
Conflict resolution preserved upstream's removal of NuGet Dependabot scans and
kept both the Aspire and CI sections in `CONTRIBUTING.md`; product source did not
conflict.

## Observed existing diagnostics

The reference host's `IdentityBootstrapDiagnostic` reports an empty persisted user
store even though this host selects configuration-based users. Sign-in and a
protected request were verified despite that message; the guide explains it.

Initial HTTPS requests also produced transient Kestrel `TimingPipeFlusher` errors
despite successful responses. They did not recur in workflow requests after two
ordinary restarts. Their cause was not established, and no unrelated runtime or
repository-wide async policy changes were made.

## Follow-up: relocation and current main (2026-11-15)

Merged `origin/main` at `16c06213d`, preserving its removal of the modular host and
associated modules. The AppHost now lives physically at `src/apps/Elsa.AppHost`;
the explicit command target is `src/apps/Elsa.AppHost/Elsa.AppHost.csproj`.

The generated PostgreSQL volume identity depends on the AppHost path, so the
relocated AppHost uses a different volume. The original volume and its data are
left intact; no data migration is implied by the move. The historical restart
check above applies to the original path.

The standalone ServiceDefaults test was subsequently removed at the user's
request; the existing host smoke tests remain.

| Follow-up check | Result |
| --- | --- |
| Full-target and locked solution restores; solution build after merge and relocation | Passed; build completed with 189 existing warnings and zero errors. |
| AppHost MSBuild boundary | `TargetFramework=net11.0`, empty `TargetFrameworks`, no inherited Fody/JetBrains references; dependency lock is byte-identical. |
| Current host smoke tests on .NET 10 and .NET 11 | Rebuilt and passed after removing the standalone ServiceDefaults test: eight passed, four per framework, zero failures/skips. Historical counts above describe earlier revisions. |
| Isolated Aspire start and PostgreSQL/server readiness at the new path | Passed using `src/apps/Elsa.AppHost/Elsa.AppHost.csproj`. |
| Discovered server endpoint health and HTTP workflow demo | HTTP and HTTPS checks passed with the expected workflow response; request/workflow spans reached Aspire without workflow faults. |
| Data volume preservation | New path uses a different generated volume; the original volume was confirmed to still exist. |
| Review of relocated project/configuration/documentation paths | Independent review passed; selectors, sibling server reference, solution membership, and physical location agree. |
