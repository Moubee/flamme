# Documentation

Read in importance order below, then follow the guide for the area you are changing.
Ranks apply to every Markdown file in the named group. Feature-specific contracts
and migrations take priority when working on that feature.

| Rank | Files | Purpose |
| --- | --- | --- |
| 1 — Entry and policy | [README](../README.md), [CONTRIBUTING](../CONTRIBUTING.md), [SECURITY](../SECURITY.md), [AGENTS](../AGENTS.md) | Project scope, supported tooling, contribution checks, security reporting, and repository constraints. |
| 1 — Direction | [Project direction](project-direction.md), [ROADMAP](../ROADMAP.md) | Fork priorities, supported capabilities, and planned work. |
| 2 — Development and operations | [Aspire development](aspire-development.md), [CI](ci.md), [reference server](../src/apps/Elsa.Server.Web/README.md), [build/run](wiki/build-run-operate.md), [health checks](wiki/health-checks.md), [telemetry](wiki/opentelemetry-workflows.md) | Current startup, configuration, validation, and operational behavior. |
| 2 — Upgrade guidance | [Persistence migration](postgres-inmemory-migration.md), [integration migration](integration-removal.md), [BPMN migration](wiki/bpmn-workflows.md), [migration guides](migrations), [migration scripts](../scripts/migrations/README.md) | Required actions when upgrading hosts, stored data, permissions, and authentication. |
| 2 — Domain contracts | [Domain language](domain-language.md), [User Tasks](user-tasks.md), remaining `src/**/README.md` files | Canonical terminology and module-specific usage or security constraints. |
| 3 — Architecture and implementation | [Contributor wiki](wiki/README.md), remaining `doc/wiki/*.md`, [ADR index](adr/toc.md) and `doc/adr/*.md` | Source maps and durable design decisions. The ADR index is generated. |
| 3 — Test guidance | [Testing strategy](qa/test-guidelines.md), `test/**/README.md` | Test boundaries, fixtures, and scenario-specific setup. |
| 4 — Supporting context | [Throughput analysis](qa/workflow-throughput-log-persistence-analysis.md), [historical finding reconciliation](security-assessment/README.md) | Focused research and dated evidence with explicit validation limits. |
| 4 — Contribution support | `.github/ISSUE_TEMPLATE/*.md`, [PR template](../.github/pull_request_template.md), [Copilot pointer](../.github/copilot-instructions.md) | Submission templates and agent entry point. |

## Keeping the repository clean

Keep one maintained source for each topic. Put reusable guidance in the wiki or a
module README and architectural rationale in an ADR. Keep task transcripts, build
logs, review ledgers, generated codebase scans, and one-off plans in PRs or external
evidence instead of adding parallel Markdown reports.

Feature specifications, Claude folders, SpecKit generation scaffolding,
retired project-local skills, upstream announcement packs, superseded scans,
and execution reports were removed. Their earlier versions remain in Git history.
The former root `CONTEXT.md` glossary is maintained as [domain language](domain-language.md).

Use Aspire for the coordinated local server/database setup. The `docker/`
directory retains server image builds and their entrypoint; stale Compose and
Kubernetes examples and retired release automation were removed. Kubernetes
deployment guidance remains [roadmap work](../ROADMAP.md#priority-3-make-kubernetes-the-primary-operational-path).
