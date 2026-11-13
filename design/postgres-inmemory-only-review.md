# PostgreSQL and in-memory persistence review

Reviewed implementation: `8650f0563` on `chore/postgres-inmemory-only`, rebased
onto the native .NET build migration in `a26912f8c`.

## Completed steps

1. `97d7c277e` — record the decomposition and acceptance criteria.
2. `029f8dc06` — remove the complete vNext persistence system.
3. `d9e62a377` — remove unsupported provider implementations.
4. `8650f0563` — migrate hosts and meaningful tests, remove obsolete fixtures
   and dependencies, and update tooling and documentation.

Independent source review began after the third step. Two reviewers verified
the integrated changes after the fourth step. No actionable findings remain.

## Findings resolved

- Preserve relational conformance coverage by running PostgreSQL automatically
  after deleting the SQLite fixtures.
- Keep the User Tasks PostgreSQL model compatible with the existing JSON-text
  tags column, including mutable-list change tracking and literal tag search.
- Restrict the tag-search SQL subquery to the requested tenant, parameterize
  search values, and quote and escape configured SQL identifiers. Regression
  cases cover punctuation, braces and quotes in schema names, authorization,
  tenant isolation, totals, and pagination.
- Replace the unused multi-database FluentMigrator runner dependency with the
  migration-types package.
- Resolve migration-script project paths relative to the repository, and derive
  the CI coverage result count from the remaining solution filter.

## Original validation

These results were recorded before the rebase, at the original implementation
commit `ba1e1dc80`. The commit hashes above identify the same steps after rebase.

- Full solution build passed across the configured frameworks with zero errors.
  Existing dependency audit, documentation, and nullable warnings remain.
- Final full solution test run: **4,599 total, 4,597 passed, zero failed, two
  existing component skips**. The final run used:

  ```sh
  dotnet test --solution Elsa.sln --no-build --no-restore --max-parallel-test-modules 2 --verbosity quiet -- --report-trx
  ```

  `ELSA_TEST_POSTGRES` pointed to a disposable validation server. The optional
  User Tasks database override was unset, exercising automatic database creation.
- Final User Tasks checks: 117 conformance cases and seven EF cases passed,
  including both supported backends and the new PostgreSQL regressions.
- All 139 project manifests and both solution filters resolve. No removed
  backend or vNext project/package references remain. Production dependency
  asset graphs contain no removed database driver packages.
- Existing PostgreSQL migrations are unchanged.
- Migration scripts pass shell syntax checks; six non-reset scripts resolve the
  PostgreSQL project from outside the repository with commands stubbed. No
  migration files were regenerated during this validation.
- `git diff --check` and the ADR table-of-contents check pass.

Current support and upgrade behavior are documented in
[the migration guide](../doc/postgres-inmemory-migration.md). Historical release,
QA, and security records retain their original evidence with superseding notes.

## CI follow-up after rebase

- Rebased onto `a26912f8c`, retaining the native .NET workflows, SDK selection,
  and separately hosted Studio guidance. Both documentation conflicts are resolved.
- Linux CI exposed two AI persistence assertions that compared .NET 100-nanosecond
  timestamps exactly with PostgreSQL microsecond timestamps. `fe9ccce57` aligns
  the fixture inputs to database precision without changing production behavior
  or weakening the creation, update, and retention equality assertions.
- The complete AI EF suite passed in Release: 36 tests, zero failures or skips.
- The old Claude review failure required an uninstalled GitHub App. Current
  `main` removes that workflow; the rebase preserves that removal.
- All 137 remaining project manifests resolve; the solution filters still select
  58 unit/integration projects and one component project. The smaller manifest
  count reflects the build and sample projects removed on `main`.
