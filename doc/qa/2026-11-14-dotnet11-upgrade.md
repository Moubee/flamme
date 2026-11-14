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
