# Integration removal review

## Checkpoint after three commits

Reviewed on 2026-11-14 after:

1. `6823deab0` — scope and acceptance plan.
2. `0e310a77f` — BPMN, WebhooksCore and Platform recipe retirement.
3. `7b6cc6473` — modular host and shell API/client retirement.

Two implementation agents independently reviewed code outside their original
scope. The coordinator checked the source and integrated the findings below.
This checkpoint precedes completed shell removal and build/restore validation.

| Priority | Finding | Resolution |
| --- | --- | --- |
| P1 | The solution and central package declarations still listed the retired Webhooks module. | Removed its solution entry and WebhooksCore declaration during graph integration. |
| P2 | Migrating a shell named `Default` to classic hosting changes the persisted pause key to lowercase `default`; startup could otherwise dispatch work despite the old administrative pause. | Added a stopped-runtime key migration and pause-recovery gate to the migration guide. Automatic merging of multiple shell pause states is deliberately avoided. |
| P2 | Active BPMN/wiki instructions still advertised removed routes and source paths. | Replace current instructions with a retirement/migration notice; preserve historical specifications and ADRs. |
| P3 | Permission-guide tests previously received retained module descriptors transitively through the removed modular host. | Added explicit retained module references to keep catalog completeness coverage. |

Static review confirmed that all 340 retained project references resolved at this
checkpoint and that 26 inspected declarations for the exception groups retained
their baseline versions. Script, AI, blob, generic HTTP, RunTask and classic
runtime drain/pause implementations remain. Package counts and deletion counts
are not performance measurements.

The remaining shell adapter deletions require an explicit scope clarification:
automatic approval review interpreted their removal as deleting retained
scripting, AI, persistence, resilience and FastEndpoints capabilities. The
coordinator asked for approval of CShells-only adapter deletion while retaining
the corresponding classic implementations and dependencies. No rejected bulk
deletion is treated as completed work.

## Final verification

Pending completion of shell adapters, full-target restore/lock regeneration,
Release build, retained runtime/API/tenant/host tests, and restored dependency
graph inspection. The final evidence will distinguish passing checks from
infrastructure-dependent or unrun checks.
