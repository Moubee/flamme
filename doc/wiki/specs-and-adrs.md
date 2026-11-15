# Specs And ADRs

The repository carries two useful design-history systems:

- ADRs in [doc/adr](../adr), which document durable architecture decisions.
- Feature specs in [specs](../../specs), which document planned and recently implemented feature work.

Use both before making architectural changes. Specs often explain the "why now"; ADRs explain decisions intended to outlive a single feature.

Feature folders retain their design records, including draft proposals and historical
implementation tasks. Verify behavior against current code and tests. The SpecKit
generator, local agent skills, and constitution templates have been retired; old
command and constitution references in these records are historical. Specifications
for removed shell hosting and custom diagnostics are available in Git history.

## ADR Index

The table of contents is [doc/adr/toc.md](../adr/toc.md).

New ADRs use date-prefixed filenames; preserve the legacy numbered records.
Follow [CONTRIBUTING.md](../../CONTRIBUTING.md#architecture-decision-records)
to add a decision and regenerate the index.

## Active And Recent Specs

| Spec | Area | Why it matters |
| --- | --- | --- |
| [002 graceful shutdown](../../specs/002-graceful-shutdown/spec.md) | Runtime | Defines quiescence, ingress sources, drain orchestration, interrupted recovery, and runtime admin endpoints. |
| [006 state machine activity](../../specs/006-state-machine-activity/spec.md) | Workflow core | Adds a state machine activity with named states and trigger-driven transitions to the workflow engine. |
| [007 secrets module](../../specs/007-secrets-module/spec.md) | Secrets | Revamps the secrets module with named secrets, pluggable stores, extensible secret types, secret picker UX, permissions, import/export encryption support, and migration from existing sensitive fields. |
| [008 Weaver AI copilot](../../specs/008-weaver-ai-copilot/spec.md) | AI | Implements Weaver, an agentic workflow assistant with streaming chat, context resolution, reviewable proposals, audit, and Studio integration. |
| [009 operational dashboard](../../specs/009-operational-dashboard/prd.md) | Dashboard API | PRD for a read-only backend dashboard API module exposing workflow activity aggregates, health signals, and operational summaries without requiring Studio to orchestrate many separate requests. |
| [010 workflow JSON hardening](../../specs/010-workflow-json-hardening/spec.md) | Workflow core | Introduces dedicated type aliases for workflow JSON, rejects unknown/unsafe CLR names, and preserves backward-compatible reads for selected legacy identifiers. |
| [012 output converters](../../specs/012-output-converters/spec.md) | Workflow core | Extensible, explicitly-identified output converters that transform an activity's native output at the binding boundary before writing the destination variable or workflow output. |
| [012 external authentication](../../specs/012-external-authentication/spec.md) | Security | Server-brokered external identity providers: Identity Provider Connections, OpenID Connect adapter, linked identity resolution, configurable unlinked-identity policies, and EF Core persistence across all providers. |
| [012 weaver grounding tools](../../specs/012-weaver-grounding-tools/spec.md) | AI | Grounds Weaver in real Elsa server data: activity registry discovery, workflow definition inspection, instance and incident investigation, and proposal-based workflow authoring with validation. |
| [013 user tasks](../../specs/013-user-tasks/spec.md) | Workflow core | Identity-neutral durable human-task module: workflow pause/resume for human decisions, a secure task queue, typed outcomes, and a task workbench. No dependency on `Elsa.Identity`. |
| [013 RBAC authorization model](../../specs/013-rbac-authorization-model/spec.md) | Security | Structured role-based access-control model on top of the two-axis permission system (ADR 0025). Status: Draft — pending approval. |

Each spec folder usually contains:

- `spec.md`: product/user-facing requirements
- `plan.md`: architecture and implementation plan
- `research.md`: decisions and tradeoffs
- `data-model.md`: domain model
- `contracts`: API/provider contracts
- `quickstart.md`: usage validation
- `tasks.md`: implementation backlog
- `checklists/requirements.md`: requirement quality checks

## Reading Order For Runtime Work

For runtime behavior, read in this order:

1. [Workflow Runtime wiki page](workflow-runtime.md)
2. [specs/002-graceful-shutdown/plan.md](../../specs/002-graceful-shutdown/plan.md)
3. [ADR 0003](../adr/0003-direct-bookmark-management-in-workflowexecutioncontext.md)
4. [ADR 0004](../adr/0004-activity-execution-snapshots.md)
5. affected runtime service and tests

## Reading Order For Flowchart Work

1. [Workflow Core wiki page](workflow-core.md)
2. [ADR 0005](../adr/0005-token-centric-flowchart-execution-model.md)
3. [ADR 0007](../adr/0007-adoption-of-explicit-merge-modes-for-flowchart-joins.md)
4. [Flowchart activities](../../src/modules/Elsa.Workflows.Core/Activities/Flowchart/Activities)
5. flowchart unit/integration tests

## Reading Order For Tenancy Work

1. [Identity, Tenancy, And Security](identity-tenancy-security.md)
2. [ADR 0008](../adr/0008-empty-string-as-default-tenant-id.md)
3. [ADR 0009](../adr/0009-asterisk-sentinel-value-for-tenant-agnostic-entities.md)
4. tenant feature and persistence code
5. tenant unit tests

## Reading Order For Diagnostics Work

1. [OpenTelemetry Workflow Instrumentation and migration](opentelemetry-workflows.md)
2. [WorkflowInstrumentation](../../src/modules/Elsa.Workflows.Core/Telemetry/WorkflowInstrumentation.cs)
3. [Workflow instrumentation tests](../../test/unit/Elsa.Workflows.Core.UnitTests/Telemetry/WorkflowInstrumentationTests.cs)
4. [Testing Guide](testing-guide.md) for TUnit-owned logs and telemetry

Hosts configure the official OpenTelemetry packages and external backends. The
retired diagnostics implementations and their specifications are available in Git history.

## Reading Order For Secrets Work

1. [Identity, Tenancy, And Security](identity-tenancy-security.md) — Secrets section
2. [specs/007-secrets-module/spec.md](../../specs/007-secrets-module/spec.md)
3. [specs/007-secrets-module/plan.md](../../specs/007-secrets-module/plan.md)
4. `Elsa.Secrets` feature and contracts
5. secrets unit tests

## Reading Order For Output Converters Work

1. [Output Converters wiki page](output-converters.md)
2. [ADR 0011](../adr/0011-output-conversion-at-binding-is-synchronous.md)
3. [ADR 0012](../adr/0012-output-converters-use-explicit-stable-identities.md)
4. [ADR 0013](../adr/0013-output-converter-discovery-is-server-owned.md)
5. [specs/012-output-converters/plan.md](../../specs/012-output-converters/plan.md)
6. output converter contracts and implementation in `Elsa.Workflows.Core`

## Reading Order For AI Copilot Work

1. [specs/008-weaver-ai-copilot/spec.md](../../specs/008-weaver-ai-copilot/spec.md)
2. [specs/008-weaver-ai-copilot/plan.md](../../specs/008-weaver-ai-copilot/plan.md)
3. `Elsa.AI.Abstractions` contracts
4. `Elsa.AI.Host` feature and endpoints
5. `Elsa.AI.Copilot` adapter and options
6. AI unit and integration tests

## Reading Order For External Authentication Work

1. [specs/012-external-authentication/spec.md](../../specs/012-external-authentication/spec.md)
2. [specs/012-external-authentication/plan.md](../../specs/012-external-authentication/plan.md)
3. [Identity, Tenancy, And Security](identity-tenancy-security.md)
4. `Elsa.ExternalAuthentication` feature and contracts
5. `Elsa.ExternalAuthentication.OpenIdConnect` adapter
6. `Elsa.ExternalAuthentication.Persistence.EFCore` and provider packages

## Reading Order For User Tasks Work

1. [specs/013-user-tasks/spec.md](../../specs/013-user-tasks/spec.md)
2. [specs/013-user-tasks/plan.md](../../specs/013-user-tasks/plan.md)
3. [ADR 0026](../adr/0026-identity-neutral-user-task-participants.md) — identity-neutral participant references
4. [ADR 0027](../adr/0027-project-user-tasks-from-committed-bookmarks.md) — bookmark-projection model
5. `Elsa.UserTasks` feature, contracts, and stores
6. `Elsa.UserTasks.Persistence.EFCore` and `Elsa.UserTasks.Persistence.EFCore.PostgreSql`

## Migrating Removed BPMN Support

BPMN execution and interchange are no longer included. Read the
[BPMN migration notice](bpmn-workflows.md) and the
[integration removal guide](../integration-removal.md) before upgrading a host
with BPMN-backed definitions or suspended instances. The migration guide and ADRs
preserve the removal rationale; historical implementation specifications are available
in Git history.

## When To Write An ADR

Write or update an ADR when a decision:

- changes workflow execution semantics
- changes persisted data conventions
- changes tenant/security behavior
- introduces a durable architectural boundary
- rejects an obvious alternative that future contributors may ask about
- affects multiple modules or provider packages

Feature-specific decisions can stay in `specs/*/research.md` unless they are expected to outlive the feature or guide unrelated future work.
