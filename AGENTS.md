# Flamme Agent Guide

Flamme is a .NET process orchestrator forked from Elsa 3.9. Existing `Elsa.*`
technical identifiers remain unchanged. Read [CONTRIBUTING.md](CONTRIBUTING.md)
for build, test, dependency, and pull request workflows; use the SDK selected by
[global.json](global.json).

Use the [ranked documentation index](doc/README.md) to find guidance for a task.

## Project constraints

- Prioritize runtime correctness, measured performance, distributed execution,
  PostgreSQL persistence, and Kubernetes operations. See the
  [project direction](doc/project-direction.md) and [roadmap](ROADMAP.md).
- PostgreSQL is the only supported durable database. In-memory stores support
  development and tests; MySQL, SQLite, and Cassandra are future plans.
- Hosts use static `AddElsa` composition. Nuplane, CShells adapters/reload APIs,
  Loom Platform recipes/catalog manifests, BPMN, and WebhooksCore are retired.
  See the [integration migration guide](doc/integration-removal.md).
- Preserve C#, JavaScript, Python, and Liquid scripting, AI/blob, FastEndpoints,
  Fody, Newtonsoft.Json, distributed locks, resilience, and PostgreSQL/in-memory
  persistence support. The ElsaScript workflow DSL is retired; see its
  [migration guide](doc/migrations/elsascript.md).
- Keep modules under `src/modules/`, shared infrastructure under `src/common/`,
  and runnable hosts under `src/apps/`. Cross-module dependencies use public
  contracts; follow existing activity metadata and `Input<T>`/`Output<T>` patterns.
- Use async I/O and existing execution pipelines and mediator contracts. Measure
  performance changes with reproducible workloads and before/after results.

## Editing

- Keep changes focused on the request and preserve unrelated user edits.
- Preserve public APIs unless the task requires a change. Document externally
  visible behavior, configuration, and developer workflow changes.
- Follow `.editorconfig` and shared MSBuild settings. C# uses the latest language
  version, nullable reference types, and implicit usings.
- Use American English for new code and prose; preserve established public names.
- Centralize package versions in `Directory.Packages.props` and library targets
  in `src/Directory.Build.props`. Libraries, server apps, and correctness tests
  target .NET 10 and .NET 11; the Aspire AppHost targets .NET 11, and benchmarks
  target .NET 10.
- .NET 11 enables runtime async through `Directory.Build.targets`; retain the
  ConfigureAwait.Fody policy for both supported frameworks. Rebuild with
  `-p:UseRuntimeAsync=false` when verifying the opt-out; see
  [platform limits](CONTRIBUTING.md#build-and-test).

## Validation

- Run checks appropriate to the change and report results and limitations.
  Prefer targeted tests while iterating; run the complete suite for shared
  infrastructure or cross-module behavior changes.
- Use TUnit on Microsoft Testing Platform, selected by `global.json`:
  `dotnet test --project <csproj>` or `dotnet test --solution Elsa.slnx`.
  Outer runner options go before `--`; later options reach the TUnit application.
- Await TUnit assertions. Use `TUnit.Mocks` and existing shared fixtures; preserve
  async behavior, argument matching, capture, and property storage in mock changes.
- Place tests under `test/unit/`, `test/integration/`, `test/component/`, or
  `test/performance/` as appropriate. Verify durable persistence changes against
  PostgreSQL; in-memory tests do not establish database guarantees.
- Coverage uses the Microsoft Testing Platform extension and aggregate CI
  thresholds. Do not add per-project Coverlet settings.
- Executable entry points commit `packages.lock.json`; CI restores in locked
  mode. After dependency changes, restore all targets with
  `dotnet restore Elsa.slnx --force-evaluate` and review lock updates. Restore all
  targets before a `--framework <tfm> --no-restore` test run to avoid rewriting locks.
- Use the [Aspire development guide](doc/aspire-development.md) for local server
  orchestration, endpoint discovery, telemetry, and worktree isolation.

## Architecture decisions

- Keep ADRs in `doc/adr/`. New filenames use `YYYY-MM-DD-slug.md`; the heading
  contains the title without a numeric prefix. Preserve legacy `0001`–`0027`
  filenames and headings.
- Generate `doc/adr/toc.md` with `scripts/adr/generate-toc.sh` after adding or
  retitling an ADR. Never edit the index by hand; validate with
  `scripts/adr/generate-toc.sh --check`.

Use Conventional Commits and review the final diff for unrelated changes before
handing it off.
