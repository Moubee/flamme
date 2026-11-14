# .NET 10 and 11 upgrade

Base: `origin/main` at `2b04e5a3f` (PostgreSQL and in-memory persistence only).

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
