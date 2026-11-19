# Contributing to Flamme

Flamme focuses on process orchestration for high-load, distributed production systems. Read the [project direction](doc/project-direction.md) and [roadmap](ROADMAP.md) when proposing work.

Contributions to runtime correctness, measured performance, PostgreSQL persistence, Kubernetes operations, tests, and documentation are welcome. UI and visual-designer work are secondary. Existing `Flamme.*` identifiers remain in the source; see the [README](README.md) for the project's origin and current scope.

## Development workflow

Pull requests target `main`. Keep each change focused so its behavior and verification are easy to review.

1. Fork the [Flamme repository](https://github.com/Moubee/flamme) and create a branch from the current `main`.
2. Implement the change, adding or updating relevant tests when behavior changes.
3. Update documentation for public APIs, configuration, or operational behavior.
4. Run the checks appropriate to the change and report the results.
5. Make commits in [Conventional Commits](https://www.conventionalcommits.org/en/v1.0.0/) style, such as `docs: clarify persistence scope` or `fix(runtime): preserve workflow recovery state`.
6. Open a pull request describing the problem, resulting behavior, and verification.

### Build And Test

Install the SDK selected by [global.json](global.json) (.NET SDK 11.0.100 with
patch roll-forward), plus the .NET 10 runtime to run both supported targets.
Libraries, app hosts, and correctness tests target .NET 10 and .NET 11; performance
benchmarks target .NET 10. .NET 8 and .NET 9 are no longer supported.

.NET 11 server builds enable [runtime async](https://learn.microsoft.com/dotnet/core/whats-new/dotnet-11/runtime)
centrally through the compiler's `runtime-async=on` feature. .NET 10 keeps the
compiler-generated state machines. The existing ConfigureAwait.Fody policy
applies to both models. No preview language version or environment switch is
required. Runtime async remains a preview feature in the stable .NET 11 SDK.

Use `-p:UseRuntimeAsync=false` on build, test, or pack commands to rebuild with
the previous async compilation model. Rebuild affected projects when switching
models; `--no-build` reuses whichever assemblies were built previously. For example:

```bash
dotnet test --project test/unit/Flamme.Mediator.UnitTests/Flamme.Mediator.UnitTests.csproj --configuration Release --framework net11.0 --no-restore -p:UseRuntimeAsync=false
```

The default opt-in excludes Mono, browser/WASI runtime identifiers, and
platform-specific target frameworks. The .NET 11 NuGet assets are intended for
supported CoreCLR consumers; consumers on unsupported runtimes should use the
.NET 10 assets or rebuild with runtime async disabled. NativeAOT and ReadyToRun
remain supported compilation modes.

```bash
dotnet restore Flamme.slnx
dotnet build Flamme.slnx --configuration Release --no-restore
dotnet test --solution Flamme.slnx --configuration Release --no-build
```

While iterating, use `dotnet test --project <test.csproj>` to select a test
project. Add `--framework net10.0` or `--framework net11.0` to run one target.
Create versioned packages with:

```bash
dotnet pack Flamme.slnx --configuration Release --output packages -p:Version=3.9.0-preview.1
```

Packing restores and builds implicitly. Reuse existing outputs with `--no-build`
only after building the same configuration and version. Packages are written to
`packages/`; build and intermediate outputs remain in each project's `bin/` and
`obj/` directories. Builds default to Debug and CI commands explicitly select
Release; `--configuration` overrides the default. Use `dotnet clean Flamme.slnx` to
clean outputs for the selected configuration.

See [Build, Run, And Operate](doc/wiki/build-run-operate.md) for targeted commands,
server startup, and operational guidance.

### Local development with Aspire

Use Aspire CLI 13.6.5 to start the reference server and PostgreSQL together, with
logs, traces, metrics, and health in the dashboard. Run `aspire run --isolated` from the
repository root for an interactive session. For agents or Git worktrees, use:

```bash
aspire start --non-interactive --isolated --apphost src/apps/Flamme.AppHost/Flamme.AppHost.csproj
aspire wait server --non-interactive --apphost src/apps/Flamme.AppHost/Flamme.AppHost.csproj
```

The [Aspire development guide](doc/aspire-development.md) covers prerequisites,
endpoint discovery, server health checks, rebuilding a resource, and stopping
the AppHost while retaining PostgreSQL data.

### CI validation and package artifacts

Pull requests and manual PR workflow runs validate .NET on both supported
frameworks. The required `ubuntu-latest` check succeeds only when the complete
.NET test matrix passes.

Packages runs automatically after a push to `main`; development branches can request it from
Actions → Packages → Run workflow. Select the branch and leave `build_packages`
enabled to produce NuGet artifacts, or disable it for coverage validation only.
All four coverage test lanes and the aggregate coverage threshold run either way.
Manual requests stay independent; newer automatic pushes cancel superseded runs.
See the [CI guide](doc/ci.md) for workflow policy, dependency locks, and artifacts.

### Dependency updates

[Renovate](renovate.json) manages NuGet packages, the .NET SDK and tools, npm
manifests, GitHub Actions (including composite actions), and Docker base images.
The hosted app discovers manifests automatically. Routine updates can open on
Mondays in Minsk after a seven-day release delay, with up to two open update
PRs. Each GitHub Action or reusable workflow gets its own major-update PR
automatically. Use the Dependency Dashboard to approve other proposed major
updates. Moving Microsoft runtime packages to a new target-framework major
requires an intentional framework migration.

Review dependency PRs against the upstream release notes and the existing
required CI checks. Renovate groups related packages and leaves merging to
maintainers. Agents investigating a failed update should identify the failing
dependency, check its supported frameworks, and report the commands and
results in the PR. Keep fixes on the update PR where possible. Renovate
documents how [maintainer edits affect rebases](https://docs.renovatebot.com/updating-rebasing/).

- For NuGet or SDK changes, check the complete lockfile diff and run
  `dotnet restore Flamme.slnx --locked-mode`. If locks need regeneration, run
  `dotnet restore Flamme.slnx --force-evaluate` for all targets first. Both .NET
  lanes in the PR workflow must pass.
- For npm changes, run `npm ci` and the package's build script in the affected
  directory. Review and commit any changes to bundled assets shipped from Git.
- When updating OpenTelemetry auto-instrumentation, update the release version
  and both upstream SHA-256 values in the Datadog Dockerfile together. Build
  for both supported Linux architectures when validating an archive change.

To activate the [free hosted app](https://github.com/apps/renovate), grant it
access to `Moubee/flamme`. Keep the GitHub dependency graph and Dependabot
alerts enabled so Renovate can open vulnerability-fix PRs. Those PRs can open
immediately, outside the routine schedule and PR limit. After Renovate is
active and can read alerts, disable **Dependabot security updates** in the
repository settings to complete the handoff. This setting is independent of
the removed Dependabot version-update configuration.

### Architecture Decision Records

Records live in `doc/adr/`. Name a new one `YYYY-MM-DD-slug.md` and give it a `# ` heading with the title
alone — no numeric prefix. The older `NNNN-` records stay as they are; do not renumber them, and do not
continue the sequence. Sequential numbering had no reservation step, so two branches in flight always
picked the same number and one of them had to be renumbered on merge.

`doc/adr/toc.md` is generated. After adding or retitling a record, run:

```bash
scripts/adr/generate-toc.sh
```

and commit the result. CI runs `scripts/adr/generate-toc.sh --check` and fails a pull request whose index
is out of date, so the index is never hand-edited.

## Pull requests

Keep one concern per pull request. Put unrelated refactoring, formatting, and dependency upgrades in separate changes.

A useful description includes:

- The concrete problem and expected behavior.
- Steps to reproduce a bug or exercise the new behavior.
- Tests or other checks run, including any relevant prerequisites or limitations.
- Configuration, API, migration, or operational implications.

Performance changes should include reproducible workloads, environment details, and before/after measurements. Distributed-runtime changes should explain the concurrency or failure scenario tested, such as competing workers, interrupted execution, or rolling restarts. See the [roadmap](ROADMAP.md) for measurement and reliability priorities.

For persistence changes, verify PostgreSQL behavior when durable storage is involved. In-memory tests do not establish database transaction, migration, restart, or concurrency guarantees. MySQL, SQLite, and Cassandra are future plans rather than currently supported providers; see the [migration guide](doc/postgres-inmemory-migration.md).

## Reporting bugs

For a suspected vulnerability, follow the [security policy](SECURITY.md) and report privately instead of posting security details in an issue.

Use [Flamme issues](https://github.com/Moubee/flamme/issues) and include:

- A clear summary, expected behavior, and actual behavior.
- Reproduction steps and a minimal workflow or sample where practical.
- Relevant versions, hosting topology, persistence configuration, and workload details.
- Logs or diagnostics with secrets and sensitive process data removed.

For load-related failures, include concurrency, replica count, queue behavior, and resource observations when available.

## Feature proposals and priorities

Describe the production scenario, the behavior needed, and how the result could be verified. Substantial proposals benefit from an issue discussion before implementation.

Priority goes to process-orchestration correctness, measured performance, distributed reliability, Kubernetes operations, and PostgreSQL hardening. Scope and maintainer capacity determine sequencing; there are no guaranteed delivery dates. A focused proposal, reproducible case, or implementation helps move work forward.

## License

By contributing, you agree that your contributions will be licensed under the project's [MIT License](LICENSE).
