# PostgreSQL and in-memory persistence

## Goal and acceptance criteria

Support only PostgreSQL and the existing in-memory stores. Remove every other
database provider, its provider-specific tests and dependencies, and the entire
experimental persistence vNext system, including its PostgreSQL adapter.

The finished solution must have no references to removed projects, packages,
provider registrations, or database-specific test infrastructure. Sample hosts
and current documentation must describe supported configuration. PostgreSQL
migrations and meaningful in-memory and PostgreSQL coverage must remain.

## Commit-sized steps

1. Record the scope, ownership, acceptance criteria, and validation plan.
2. Remove persistence vNext modules, adapters, tests, and dedicated design files.
3. Remove unsupported provider implementations and their tests; retain useful
   coverage for PostgreSQL and in-memory stores.
4. Update hosts, solution and dependency manifests, build/CI configuration, and
   documentation to the remaining persistence options.

After these steps, review the combined changes independently, fix findings in
conventional commits, and run the applicable validation.

## Coordination

- Provider task: non-vNext persistence modules and shared EF implementation.
- Test task: tests, shared testing fixtures, and testing extensions.
- vNext task: vNext modules, dedicated tests, and dedicated design documents.
- Coordinator: worktree and branch, manifests, hosts, CI, documentation, commits,
  integration checks, and review.

## Validation

- Check the solution and all project references resolve to existing files.
- Search production code, tests, package manifests, and executable configuration
  for removed providers and vNext registrations.
- Build the solution and run affected tests, followed by the full test suite
  when the environment supports its required services.
- Review removed coverage, PostgreSQL behavior, in-memory defaults, host wiring,
  and dependency removals. Record any environmental limits explicitly.
