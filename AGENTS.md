# AGENTS.md

Guidance for AI coding agents working in this repository.

## Project Overview

- Flamme is a .NET process orchestrator forked from Elsa 3.9, centered on `Elsa.slnx`. Existing `Elsa.*` technical identifiers remain unchanged.
- Priorities are high-load performance, distributed production execution, PostgreSQL persistence, and Kubernetes hosting. UI work is secondary; see [project direction](doc/project-direction.md) and the [roadmap](ROADMAP.md).
- PostgreSQL is the only supported durable database. In-memory stores are for development and tests; MySQL, SQLite, and Cassandra are future plans, not bundled providers.
- Production code lives under `src/`.
- Tests live under `test/`, grouped by `unit`, `integration`, `component`, and `performance`.
- Build and package automation uses the .NET CLI and shared MSBuild settings in `Directory.Build.props` and `Directory.Build.targets`.

## Working Rules

- Keep changes focused on the requested task. Do not mix unrelated cleanup, formatting, or dependency updates into functional changes.
- Preserve existing public APIs unless the task explicitly requires an API change.
- Update tests when behavior changes and add tests for new behavior where practical.
- Update documentation when changing externally visible behavior, configuration, APIs, or developer workflows.
- Do not delete generated-looking, artifact, or IDE files unless the task explicitly asks for cleanup.
- If the worktree contains unrelated user changes, leave them untouched.

## Agent Operating Principles

- Do not assume, hide confusion, or flatten uncertainty; surface questions, constraints, and tradeoffs explicitly.
- Write the minimum code that solves the defined problem; do not add speculative abstractions, features, or cleanup.
- Touch only the files and behavior required for the task; clean up only issues introduced by your own changes.
- Define success criteria before implementation, then iterate until the criteria are verified or clearly state what could not be verified.

## Build And Test Commands

- Restore dependencies: `dotnet restore Elsa.slnx`
- Build the solution: `dotnet build Elsa.slnx`
- Clean build outputs: `dotnet clean Elsa.slnx`
- Create versioned packages: `dotnet pack Elsa.slnx --configuration Release --output packages -p:Version=3.9.0-preview.1`
- Run the complete repository test suite: `dotnet test --solution Elsa.slnx`
- Run a specific test project: `dotnet test --project test/unit/Elsa.Workflows.Core.UnitTests/Elsa.Workflows.Core.UnitTests.csproj`
- Run ElsaScript DSL integration tests: `dotnet test --project test/integration/Elsa.Dsl.ElsaScript.IntegrationTests/Elsa.Dsl.ElsaScript.IntegrationTests.csproj`

Install the .NET SDK selected by the root `global.json` (11.0.100 with patch roll-forward)
and the .NET 10 runtime to run tests and app hosts for both supported frameworks. Builds default
to Debug; CI commands explicitly select Release. Pass `--configuration` to override. Packages go
to `packages/`; build and intermediate outputs remain in each project's `bin/` and
`obj/` directories. Set `-p:Version=<version>` explicitly for release or preview packages.

Executable entry points (including tests and benchmarks) commit `packages.lock.json`.
CI restores with `--locked-mode`. After dependency changes, run a full-target
`dotnet restore Elsa.slnx --force-evaluate` and commit the reviewed lock updates.
Restore standalone `test/TlsSmoke/TlsSmoke.csproj` separately. For targeted tests,
restore all project targets first, then use `--framework <tfm> --no-restore`;
a single-target restore would rewrite the complete lock. See `doc/ci-performance.md`.

The root `global.json` also selects Microsoft Testing Platform (MTP). Options for the
outer `dotnet test` command go before the literal `--`; options after it are
forwarded to each TUnit test application. Prefer targeted
`dotnet test --project <csproj>` commands while iterating, then run
`dotnet test --solution Elsa.slnx` when the change affects shared infrastructure
or cross-module behavior.

## Code Style

- C# uses `LangVersion` `latest`, nullable reference types, and implicit usings from `Directory.Build.props`.
- Production libraries, app hosts, and correctness tests target `net10.0` and `net11.0`; library targets are centralized in `src/Directory.Build.props`. Performance benchmarks target `net10.0`. .NET 8 and .NET 9 are no longer supported.
- Follow `.editorconfig`: 4-space indentation for C#, file-scoped namespaces, braces required, `var` preferred, usings outside namespaces, and sorted system directives.
- Prefer clear domain names over abbreviations. Keep types small and responsibilities explicit.
- Avoid suppressing warnings unless compatibility or framework constraints make the warning unavoidable; explain the reason near the suppression.

## Repository Structure

- `src/apps/`: runnable app hosts.
- `src/clients/`: client libraries.
- `src/common/`: shared infrastructure and testing support.
- `src/extensions/`: extension packages.
- `src/modules/`: Elsa modules and feature packages.
- `test/unit/`: unit tests.
- `test/integration/`: integration tests.
- `test/component/`: component tests.
- `test/performance/`: performance tests.
- `Directory.Build.props`, `Directory.Build.targets`: shared MSBuild configuration.
- `doc/`, `design/`, and `specs/`: documentation, design assets, and feature specifications.

## Architecture Decision Records

- ADRs live in `doc/adr/`. New records are named `YYYY-MM-DD-slug.md`, and the `# ` heading carries the
  title alone with no numeric prefix. Do not continue the legacy `NNNN-` sequence: it collided on every
  concurrent branch, which is what the date prefix exists to stop.
- Records `0001`-`0027` keep their existing names and headings. Do not renumber or rename them.
- `doc/adr/toc.md` is generated. Never edit it by hand: run `scripts/adr/generate-toc.sh` and commit the
  result. `scripts/adr/generate-toc.sh --check` verifies it is current, and CI runs the same check.
- The rationale is recorded in
  [Identify new ADRs by date instead of a sequential number](doc/adr/2026-08-25-date-prefixed-adr-identifiers.md).

## Testing Guidance

- Place tests near the relevant existing test project rather than creating a new project by default.
- Use the existing shared testing helpers under `src/common/Elsa.Testing.Shared*` when they fit the scenario.
- Write tests with TUnit attributes such as `[Test]` and `[Arguments(...)]`; use
  asynchronous assertions such as `await Assert.That(actual).IsEqualTo(expected)`.
- Use `TUnit.Mocks` for generated mocks. Create `Mock.Of<T>().Object` for dependencies,
  and use `Mock.Get(instance)` for setups and verification. Preserve async return,
  argument capture, property storage, and generic argument matching when changing mocks.
- For regressions, add a failing test that demonstrates the bug before or alongside the fix.
- For multi-targeting issues, consider whether the test must run against all target frameworks or only the affected one.
- Repository coverage uses the Microsoft Testing Platform coverage extension and
  is evaluated as an aggregate in CI. Do not add per-project Coverlet settings or
  thresholds.

## Dependency And Package Guidance

- Central package versions are managed in `Directory.Packages.props`; do not add ad hoc versions to individual project files unless the repository already uses that pattern for the package.
- Keep target framework changes centralized in `src/Directory.Build.props` unless a project has a specific reason to differ.
- Be cautious with package upgrades because this repository targets multiple frameworks and modules.

## Review Checklist

Before handing off changes, verify the following when applicable:

- The project or solution builds.
- Relevant unit or integration tests pass.
- Public API or behavior changes are documented.
- New code follows nullable annotations and existing style.
- No unrelated files were changed.

<!-- SPECKIT START -->
For additional context about technologies to be used, project structure,
shell commands, and other important information, read `specs/013-user-tasks/plan.md`.
<!-- SPECKIT END -->

## Active Technologies
- C# latest, nullable reference types enabled, implicit usings enabled. + `Microsoft.Extensions.Logging`, `Microsoft.AspNetCore.SignalR`, Elsa feature/module infrastructure, FastEndpoints through Elsa API endpoint patterns, existing Elsa identity/authorization features. (003-live-server-logs)
- Bounded in-memory ring buffer for MVP; no EF Core schema changes. Provider abstraction allows external/shared log backends later. (003-live-server-logs)
- C# latest, nullable reference types enabled, implicit usings enabled. + `Microsoft.Extensions.Logging`, `Microsoft.Extensions.Options`, `Microsoft.AspNetCore.SignalR`, Elsa feature/module infrastructure, FastEndpoints through Elsa API endpoint patterns, CShells shell feature infrastructure. (004-diagnostics-structured-logs)
- Existing bounded in-memory ring buffer; no EF Core schema changes. Provider abstraction remains available for future shared backends. (004-diagnostics-structured-logs)
- C# latest, nullable reference types enabled, implicit usings enabled. + Existing `Elsa.Diagnostics.StructuredLogs`, `Microsoft.Extensions.Logging`, `Microsoft.Extensions.Options`, `Microsoft.AspNetCore.SignalR`, Elsa feature/module infrastructure, FastEndpoints through Elsa API endpoint patterns, and optional shared relational extension contracts. (005-structured-log-persistence)
- Bounded in-memory structured log storage; no bundled durable structured-log provider. (005-structured-log-persistence)
- C# latest, nullable reference types enabled, implicit usings enabled. + `Microsoft.Extensions.Options`, `Microsoft.AspNetCore.SignalR`, Elsa feature/module infrastructure, FastEndpoints through Elsa API endpoint patterns, Elsa shell feature infrastructure, and existing Elsa identity/authorization patterns. (006-diagnostics-console-logs)
- Bounded in-memory recent buffer and bounded subscriber queues by default; no durable database schema. Providers receive redacted content only. (006-diagnostics-console-logs)
- C# latest, nullable reference types enabled, implicit usings enabled. + Elsa feature/module infrastructure, FastEndpoints through Elsa API endpoint patterns, existing Elsa identity/authorization patterns, Elsa workflow input metadata, `Microsoft.Extensions.Configuration`, `Microsoft.AspNetCore.DataProtection`, EF Core persistence infrastructure, mediator notifications, and optional JavaScript expression integration. (007-secrets-module)
- In-memory store for tests/development; Elsa-managed encrypted store with EF Core persistence for production; configuration-backed read-only store for deployment-managed values. No cloud vault or OS certificate store provider in v1. (007-secrets-module)
- C# latest, nullable reference types enabled, implicit usings enabled; paired Studio Blazor/Razor module work in the Studio repository. + Elsa feature/module infrastructure, FastEndpoints through Elsa API endpoint patterns, existing identity/authorization and tenancy services, workflow definition/instance abstractions, diagnostics/log abstractions, `Microsoft.Extensions.Options`, `Microsoft.Extensions.Logging`, OpenTelemetry, `System.Text.Json`, SignalR or SSE streaming, GitHub Copilot SDK isolated behind `Elsa.AI.Copilot`, and headless Copilot CLI JSON-RPC integration. (008-weaver-ai-copilot)
- Configurable conversation/session retention with in-memory support for development and tests; durable proposal and audit stores required for MVP using Elsa persistence provider abstractions and an EF Core provider package for production. (008-weaver-ai-copilot)
- C# latest, nullable reference types enabled, implicit usings enabled. + Elsa AI abstractions/host modules, `GitHub.Copilot.SDK` isolated behind `Elsa.AI.Copilot`, Activity Registry, workflow management/runtime abstractions, existing identity/tenancy services, FastEndpoints through Elsa endpoint patterns, `System.Text.Json`, `Microsoft.Extensions.Options`, and `Microsoft.Extensions.Logging`. (012-weaver-grounding-tools)
- Existing workflow definition/runtime stores and Activity Registry are read sources; durable proposal and audit stores remain the write/governance path; no new required database schema for the grounding MVP. (012-weaver-grounding-tools)

## Recent Changes
- 012-weaver-grounding-tools: Plans governed Weaver grounding tools for installed activities, workflow definitions, workflow proposals, workflow instances, incidents, and Studio capability discovery.
- 008-weaver-ai-copilot: Captures Weaver as a server-hosted, provider-isolated AI copilot platform with Studio chat, governed tools, proposal-only workflow mutations, audit, and extensibility.
- 006-diagnostics-console-logs: Plans raw stdout/stderr console capture with redaction-before-provider boundaries, bounded in-memory recent/live buffers, REST backfill/source endpoints, and a SignalR live hub.
- Persistence support is limited to PostgreSQL and in-memory stores. The legacy providers and vNext persistence system have been removed; see `doc/postgres-inmemory-migration.md`.
- 004-diagnostics-structured-logs: Refactors the unpublished server logs module into diagnostics structured logs and preserves bounded structured `ILogger` capture.
- 003-live-server-logs: Added C# latest, nullable reference types enabled, implicit usings enabled. + `Microsoft.Extensions.Logging`, `Microsoft.AspNetCore.SignalR`, Elsa feature/module infrastructure, FastEndpoints through Elsa API endpoint patterns, existing Elsa identity/authorization features.
