# Contributing to Flamme

Flamme focuses on process orchestration for high-load, distributed production systems. Read the [project direction](doc/project-direction.md) and [roadmap](ROADMAP.md) when proposing work.

Contributions to runtime correctness, measured performance, PostgreSQL persistence, Kubernetes operations, tests, and documentation are welcome. UI and visual-designer work are secondary. Existing `Elsa.*` identifiers remain in the source; see the [README](README.md) for the project's origin and current scope.

## Development workflow

Pull requests target `main`. Keep each change focused so its behavior and verification are easy to review.

1. Fork the [Flamme repository](https://github.com/DenDeline/flamme) and create a branch from the current `main`.
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
dotnet test --project test/unit/Elsa.Mediator.UnitTests/Elsa.Mediator.UnitTests.csproj --configuration Release --framework net11.0 --no-restore -p:UseRuntimeAsync=false
```

The default opt-in excludes Mono, browser/WASI runtime identifiers, and
platform-specific target frameworks. The .NET 11 NuGet assets are intended for
supported CoreCLR consumers; consumers on unsupported runtimes should use the
.NET 10 assets or rebuild with runtime async disabled. NativeAOT and ReadyToRun
remain supported compilation modes. See the [research and validation record](doc/qa/2026-11-14-runtime-async.md)
for compatibility evidence and verification results.

```bash
dotnet restore Elsa.slnx
dotnet build Elsa.slnx --configuration Release --no-restore
dotnet test --solution Elsa.slnx --configuration Release --no-build
```

While iterating, use `dotnet test --project <test.csproj>` to select a test
project. Add `--framework net10.0` or `--framework net11.0` to run one target.
Create versioned packages with:

```bash
dotnet pack Elsa.slnx --configuration Release --output packages -p:Version=3.9.0-preview.1
```

Packing restores and builds implicitly. Reuse existing outputs with `--no-build`
only after building the same configuration and version. Packages are written to
`packages/`; build and intermediate outputs remain in each project's `bin/` and
`obj/` directories. Builds default to Debug and CI commands explicitly select
Release; `--configuration` overrides the default. Use `dotnet clean Elsa.slnx` to
clean outputs for the selected configuration.

See [Build, Run, And Operate](doc/wiki/build-run-operate.md) for targeted commands,
server startup, and operational guidance.

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

Use [Flamme issues](https://github.com/DenDeline/flamme/issues) and include:

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
