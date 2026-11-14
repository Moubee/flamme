# .NET 10 and 11 upgrade

Base: `origin/main` at `f43381a0d` (SLNX solution, PostgreSQL and in-memory persistence only).
Originally implemented on `2b04e5a3f`, then rebased after the solution and metadata cleanup merged.

## Delivery steps

1. Replace .NET 8/9 targets with .NET 10/11 for source packages, app hosts,
   and tests. Remove obsolete async LINQ package references and repair the
   testing extension's central package management. Keep the existing .NET
   11.0.100 SDK pin and Microsoft Testing Platform runner.
2. Refresh compatible dependencies, including TUnit and local tools, using
   current NuGet metadata. Keep EF Core and its PostgreSQL provider on compatible
   major versions across both target frameworks.
3. Update CI, container builds, migration scripts, and developer instructions.
   Run tests and coverage separately for each supported framework.
4. Review after the first three commits, fix findings, and record validation.

## Acceptance criteria

- No active project targets .NET 7, 8, or 9.
- Libraries, application hosts, and tests build for .NET 10 and 11.
- Existing unit and integration tests run through TUnit/Microsoft Testing
  Platform on both runtimes; container-dependent checks state prerequisites.
- The testing extension and TLS smoke application build explicitly because
  they are outside the solution.
- CI and Docker use the selected .NET 11 SDK, with coverage isolated per target.
- Dependency holds and any unverified checks are recorded below.

## Framework evaluation

MSBuild evaluated `net10.0;net11.0` for a core library, server host, test project,
and the testing extension. Performance benchmarks retain their existing .NET
10 target; this change does not run or compare benchmarks.

## Dependency decisions

Versions were checked against live NuGet metadata on 2026-11-14. Representative
updates include TUnit/TUnit.AspNetCore 1.84.0, Microsoft Testing Platform HangDump
2.5.3, OpenTelemetry 1.21.0, Jint 4.18.0, and Testcontainers 4.16.0. ASP.NET Core
and Microsoft.Extensions packages use 10.0.15 for .NET 10 and 11.0.0 for .NET 11;
the .NET 10 resilience packages follow their separate 10.11.0 release line.

Both targets use EF Core 10.0.15 and Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3.
The provider's published dependency range is `[10.0.4, 11.0.0)`, so EF Core 11 is
held until a compatible stable PostgreSQL provider is available. Targeting the
.NET 11 runtime does not require EF Core 11.

Updates stay within existing major versions where a new major would require a
separate API migration: examples include FastEndpoints 8.3.0, AutoMapper 16.3.0,
MassTransit 8.5.12, NuGet 7.9.0, and StackExchange.Redis 2.13.23. Pre-1.0 minor
migrations for Bpmn and Cronos and private-feed preview packages are held.
Existing OpenTelemetry Redis instrumentation stays on its beta release track,
updated to 1.21.0-beta.1. The unavailable Serilog.Extensions.Logging 10.0.3 pin
is corrected to its latest published stable version, 10.0.0.

System.Text.Json is provided by both supported runtimes, so redundant explicit
references in HTTP and blob-storage modules are removed in response to the
.NET 11 SDK's NU1510 package-pruning diagnostic.

## Review after three commits

An independent review of the framework, dependency, and CI commits found no
additional actionable defects. Validation identified two compatibility changes:

- TUnit.AspNetCore 1.84 honors the client's redirect options. Authentication
  contract tests now explicitly disable automatic redirects when asserting the
  broker's original response and Location header.
- [.NET 11 changes floating-point to decimal conversions](https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/11/decimal-biginteger-floating-point-conversions).
  Fluid's JSON filter can render additional digits for JavaScript floating-point
  values: `12.99` can render as `12.990000000000000213162820728`. The integration
  test now parses JSON and verifies the product ID and numeric value on both
  runtimes. No global rounding or workflow data conversion is introduced.

The updated OpenTelemetry archive also supports launching the application
directly through `instrument.sh`; the Docker entrypoint uses this supported path.

## Final validation

| Check | Result |
| --- | --- |
| Release solution build, .NET 10 and 11 | Passed, zero errors; nullable and obsolete API warnings remain |
| Unit/integration filter, .NET 10 | 4,375 passed, zero failures/skips |
| Unit/integration filter, .NET 11 | 4,375 passed, zero failures/skips |
| PostgreSQL component suite, each runtime | 220 passed, zero failures, two existing skips |
| Testing extension and TLS smoke builds | Passed on both frameworks |
| .NET 11 TUnit coverage smoke | 10 passed; Cobertura and TRX generated |
| Local tools | EF 10.0.15 and ReportGenerator 5.6.0 restored and executed |
| Native solution pack | 71 packages and 71 symbol packages; every package has .NET 10/11 assets |
| SDK vulnerability audit, including transitive dependencies | 135 projects checked; zero audit problems or vulnerable packages reported |
| .NET 11 server and Datadog container builds | Passed; OpenTelemetry 1.19.1 archive checksum verified |
| TLS/CA smoke checks, each .NET 11 image | All four passed, including enabled OpenTelemetry on the Datadog image |
| ADR TOC, workflow YAML, migration shell syntax, diff whitespace | Passed |

The existing component skips are `ImportWorkflowActivity_ShouldUpdateOtherPods`
(not implemented) and `DeleteWorkflow_Clustered` (interferes with other tests).
Local container validation covers .NET 11; CI also builds and smoke-tests the
.NET 10 variants. CI itself has not been run from this local branch.

Reproduction commands from the repository root:

```sh
dotnet restore Elsa.slnx
dotnet build Elsa.slnx --configuration Release --no-restore
dotnet test --solution Elsa.UnitIntegration.Tests.slnf --configuration Release --framework net10.0 --no-build
dotnet test --solution Elsa.UnitIntegration.Tests.slnf --configuration Release --framework net11.0 --no-build
dotnet test --solution Elsa.Component.Tests.slnf --configuration Release --framework net10.0 --no-build
dotnet test --solution Elsa.Component.Tests.slnf --configuration Release --framework net11.0 --no-build
dotnet pack Elsa.slnx --configuration Release --no-build --no-restore --output packages
dotnet package list --project Elsa.slnx --vulnerable --include-transitive --no-restore
```

The final compatibility fixes received a second independent review with no
actionable findings.

## Validation after rebasing onto the SLNX cleanup

The rebase onto `f43381a0d` preserves all 135 SLNX projects, the two solution
filters, fork package metadata, and upstream's removal of ten obsolete migration
generators. Active instructions use `Elsa.slnx`; the surviving authentication
migration generator retains its explicit .NET 10 framework selection.

- Release restore/build passed with zero errors.
- The complete SLNX suite ran both frameworks together: **9,190 passed, four
  existing skips, zero failures** across 118 test modules.
- All 63 release-helper tests passed. Workflow YAML, ADR TOC, and whitespace
  checks passed, and independent rebase review found no actionable issues.
- Packing produced 71 NuGet packages and 71 symbol packages. Every NuGet package
  retains both framework assets and the updated `DenDeline/flamme` URLs.

The first combined run encountered PostgreSQL SSL-handshake connection failures
in the seven-test .NET 10 User Tasks EF module. All seven tests passed on an
isolated retry, followed by the successful complete rerun recorded above. The
two frameworks used separate containers; no speculative fixture change was made.

The complete rerun used:

```sh
dotnet test --solution Elsa.slnx --configuration Release --no-build --max-parallel-test-modules 4 --timeout 10m -- --report-trx
```
