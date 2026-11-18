# Continuous integration

Workflow definitions in [`.github/workflows`](../.github/workflows) are the source
of truth for triggers and commands. Use the SDK selected by
[`global.json`](../global.json), with the .NET 10 runtime installed alongside it.

## Validation and artifacts

| Workflow | When it runs | What it checks |
| --- | --- | --- |
| [PR validation](../.github/workflows/pr.yml) | Pull requests and manual runs | Build and TUnit/Microsoft Testing Platform tests on .NET 10 and .NET 11. The .NET 10 lane builds the full solution, including benchmarks. |
| [Packages](../.github/workflows/packages.yml) | Pushes to `main` and manual runs | Unit/integration and component coverage on both frameworks, aggregate coverage gate, and optional build/pack artifacts. |
| [ADR index](../.github/workflows/adr-toc.yml) | ADR/index/script changes | Generated architecture decision index is current. |

Pull requests and manual PR workflow runs execute the complete .NET matrix.
The required `ubuntu-latest` result check succeeds only when both framework lanes
pass; failed, canceled, or skipped tests fail the gate.

Packages manual runs can target a development branch. `build_packages` defaults
to true; disabling it skips only the separate build/pack job. All four coverage
lanes and the aggregate 10% line-coverage gate still run. A successful artifact
upload alone does not establish a passing workflow.

Download `elsa-nuget-packages`, framework-specific test results, and
`coverage-reports` from a Packages run. Superseded automatic runs are canceled;
explicit manual Packages runs remain independent.

There is no configured public package publisher or GitHub Pages deployment.
The stale workflow manages old issues and pull requests; Copilot setup prepares
the development environment. Neither is a release publisher.

## Dependency locks and caching

Central versions live in [`Directory.Packages.props`](../Directory.Packages.props).
Executable entry points, tests, and benchmarks commit `packages.lock.json`;
library dependencies are captured by those consuming applications.

CI restores with `--locked-mode`. The `ELSA0003` build guard rejects missing
executable lockfiles during locked restore. After intentional dependency changes:

```bash
dotnet restore Elsa.slnx --force-evaluate
```

Review and commit the complete lock changes. Restore all target frameworks before
selecting one for a test run:

```bash
dotnet restore Elsa.slnx --locked-mode
dotnet test --project test/unit/Elsa.Mediator.UnitTests/Elsa.Mediator.UnitTests.csproj --framework net11.0 --no-restore
```

A single-target restore can rewrite a two-target lock or fail locked validation.
SDK patch roll-forward can also require reviewed lock changes.

The [shared cache action](../.github/actions/cache-nuget/action.yml) caches NuGet
downloads using OS, architecture, workload, framework, and restore inputs.
A cache hit never skips restore. Tool caches have separate fallbacks; compiled
`bin` and `obj` outputs are not cached.

Renovate policy and dependency PR verification instructions live in
[CONTRIBUTING.md](../CONTRIBUTING.md#dependency-updates). The configured
[`NuGet.Config`](../NuGet.Config) uses nuget.org; historical Feedz access notes
and CI timing reports are available in Git history.
