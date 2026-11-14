# Native .NET Build Migration Review

Validated on 2026-11-13 with .NET SDK 11.0.100 and MSBuild 18.12.2 on macOS arm64.
The SDK was selected from Microsoft's [release metadata](https://github.com/dotnet/core/blob/main/release-notes/releases-index.json).
Library targets remain `net8.0`, `net9.0`, and `net10.0`; tests and application hosts remain on .NET 10.

## Result

NUKE's project, dependencies, configuration, generated workflow source, and bootstrap scripts are removed.
The standard .NET CLI owns restore, build, clean, test, and pack. Shared MSBuild targets retain the
`packages/` artifact directory and enable `ContinuousIntegrationBuild` when `CI=true`.
CI explicitly selects Release and supplies the same `Version` during restore, build, and pack.
Package versions use standard MSBuild properties without an implicit timestamp suffix.

`Elsa.SamplePackage` was subsequently removed at the user's request, including its solution entries,
release-profile exception, and active documentation. Release-verifier tests use a synthetic fixture
package to retain coverage for custom fixed-version profiles.

## Review Checkpoint

An independent review followed the first three conventional commits:

1. `build: configure native dotnet packaging with SDK 11`
2. `ci: replace nuke invocations with dotnet build and pack`
3. `build: remove nuke and document native dotnet workflows`

The review identified one P2 issue: floating Docker SDK tags could advance to a feature band rejected
by `global.json`'s `latestPatch` policy. Both remaining server Dockerfiles use `sdk:11.0.100`.
Its verified manifest digest matches the SDK image used by the successful container build:
`sha256:65f3c7c6a25a239ab3e6719f2896147e7b68242ff2d8818dd70498bde6ef8bea`.

The final review also covered the sample-package removal, package versions and dependency versions,
symbol packages, output overrides, MTP runtime installation, publishing action pinning, and the
availability of `global.json` in sparse checkouts. No actionable findings remain.

## Initial Validation

| Check | Result |
| --- | --- |
| Full solution restore | Passed; existing dependencies produce NuGet audit warnings. |
| Full Release build with SDK 11 | Passed, zero errors and 190 warnings from source/analyzers. |
| Native solution pack | Passed; 102 `.nupkg` and 102 `.snupkg` files. |
| Package archive inspection | Every package uses `3.9.0-preview.1`; Elsa project dependencies use the same version. Assemblies cover .NET 8, 9, and 10. No sample package remains. |
| Full native MTP test run | 61 modules; 4,791 passed, 146 skipped, zero failed; 1 minute 26 seconds. |
| Component tests within that run | 220 passed, two skipped; 1 minute 16 seconds. |
| Release-helper Python tests | All 63 passed, including generic fixed-version verification. |
| Full Release clean | Passed; removed build outputs and retained all 204 package artifacts. |
| Server container | Full restore, build, net10 publish, and image creation passed. |
| Workflow YAML, diff whitespace, ADR index | Passed. |

Representative validation commands, run from the repository root:

```bash
dotnet restore Elsa.sln --ignore-failed-sources -m:1 -nr:false
CI=true dotnet build Elsa.sln --configuration Release --no-restore \
  -p:Version=3.9.0-preview.1 -p:UseSharedCompilation=false -m:4 -nr:false
CI=true dotnet pack Elsa.sln --configuration Release --no-build --no-restore \
  --output packages -p:Version=3.9.0-preview.1 -m:1 -nr:false
dotnet test --solution Elsa.sln --configuration Release --no-build \
  --max-parallel-test-modules 4 --timeout 10m \
  --results-directory artifacts/test-results/msbuild-migration -- --report-trx
python3 -m unittest discover -s scripts/archive/elsa-release/tests -v
dotnet clean Elsa.sln --configuration Release -p:Version=3.9.0-preview.1 -m:1 -nr:false
docker build --progress=plain -f docker/ElsaServer.Dockerfile -t flamme-msbuild-validation .
scripts/adr/generate-toc.sh --check
```

Local MSBuild-server restrictions required `DOTNET_CLI_USE_MSBUILD_SERVER=0`; the full build and tests
also needed normal process, localhost, and Docker access outside the filesystem sandbox.
These are validation-environment settings, not repository build requirements.

CI publishing and coverage deployment were not executed. The initial local container check built
the server Dockerfile; GitHub Actions subsequently validated the complete container matrix.
Dependency upgrades and existing warnings are outside this migration's scope.

## Pull Request Follow-Up

All four Docker certificate smoke jobs passed in GitHub Actions on the initial PR head.
The inherited Claude review workflow failed because the repository had no Claude GitHub App
installation or OAuth token. At the user's request, the Claude review, mention-triggered assistant,
Greploop, and wiki-refresh workflows were removed. No remaining workflow invokes Claude or
references its OAuth secret.

The initial Linux PR build also exhausted the hosted runner's disk during compilation. The check
annotation reported `System.IO.IOException: No space left on device` while writing the runner's
diagnostic log, and the worker terminated before tests. PR and package full-solution build jobs now
reclaim unused Android, Haskell, Swift, and CodeQL installations before setup and restore, while
retaining .NET and Docker. They report available disk space before and after cleanup.

The branch was then rebased onto the merged Studio-removal PR, preserving its backend-only server
configuration and deleted Studio Dockerfiles. The remaining certificate-smoke matrix covers the
server and Datadog server images. The initial validation totals above predate that upstream change.
