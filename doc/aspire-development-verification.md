# Aspire verification and review

Verified on 2026-11-14 in the separate `aspire-development` worktree, branch
`feat/aspire-development`, created from freshly fetched `origin/main` at
`8c50b8669`. Other worktrees were not edited. SDK: .NET 11.0.100. Aspire CLI,
AppHost SDK and PostgreSQL hosting integration: 13.6.5.

## Build and tests

| Check | Result |
| --- | --- |
| `dotnet restore Elsa.slnx --force-evaluate` | Passed; only affected executable lock files changed. |
| `dotnet restore Elsa.slnx --locked-mode` | Passed with committed locks. |
| `dotnet build Elsa.slnx --no-restore -m:1` | Passed for both supported frameworks; 159 warnings in existing code, zero errors. |
| `dotnet test --project test/integration/Elsa.Hosts.SmokeTests/Elsa.Hosts.SmokeTests.csproj --no-build --no-restore` | 14 passed, zero failed/skipped: seven on each framework. |
| New telemetry exporter regression | Workflow span exported; workflow counter exported with value 7 without requiring an OTLP collector. |
| `git diff --check` | Passed. |

The initial parallel solution build was cancelled while dependency setup was
taking time. The serial solution build completed, including the existing Copilot
CLI download. The complete solution test suite was not run; the relevant real-host
smoke suite covers both reference hosts and the new telemetry test.

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
  Structured startup logs were also exported. Metric export is covered by the
  exporter regression test; the dashboard metric UI was not manually inspected.
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
advanced independently during the task; review used the original merge base so
unrelated upstream changes were not absorbed into this branch.

## Observed existing diagnostics

The reference host's `IdentityBootstrapDiagnostic` reports an empty persisted user
store even though this host selects configuration-based users. Sign-in and a
protected request were verified despite that message; the guide explains it.

Initial HTTPS requests also produced transient Kestrel `TimingPipeFlusher` errors
despite successful responses. They did not recur in workflow requests after two
ordinary restarts. Their cause was not established, and no unrelated runtime or
repository-wide async policy changes were made.
