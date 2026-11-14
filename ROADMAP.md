# Flamme roadmap

Flamme is a .NET process orchestrator forked from [Elsa Workflows 3.9](https://github.com/elsa-workflows/elsa-core/releases/tag/3.9.0). Its direction is high-load, distributed production workloads, with Kubernetes as the primary hosting scenario.

The [README](README.md) introduces the project, and [Project direction](doc/project-direction.md) explains the fork and its scope. This roadmap describes priorities rather than a release schedule. Unchecked items are planned work; they are not claims of completed validation or guaranteed delivery dates.

## Current foundation

The repository already contains the following building blocks. Their presence does not establish throughput targets or prove every clustered deployment scenario.

| Area | Available foundation |
| --- | --- |
| Process orchestration | Workflow definitions in C# and JSON, composable activities, triggers, bookmarks, workflow management and runtime APIs. |
| Distributed runtime | Distributed runtime modules, coordination extension points, runtime pause/drain controls, and interrupted-workflow recovery mechanisms. |
| Durable persistence | PostgreSQL through the existing EF Core provider packages. See the [persistence migration guide](doc/postgres-inmemory-migration.md). |
| Development and tests | Process-local in-memory stores. State does not survive a restart and is not shared between replicas. |
| Operations | Container build files, runtime diagnostics, OpenTelemetry instrumentation, and separate liveness/readiness endpoints. See [health checks](doc/wiki/health-checks.md). |
| Tooling | The SDK selected by [global.json](global.json), the `Elsa.slnx` solution, native .NET CLI/MSBuild commands, and TUnit on Microsoft Testing Platform. See [build and operations](doc/wiki/build-run-operate.md). |

## Priority 1: measure and improve performance

Optimize the paths that matter for sustained process orchestration, with reproducible evidence for each improvement.

- [ ] Define representative workloads for starting, dispatching, suspending, and resuming workflows, including short processes and long-lived processes with many bookmarks.
- [ ] Establish repeatable load and benchmark scenarios with documented hardware, runtime, PostgreSQL configuration, workflow shape, and replica count.
- [ ] Track throughput, tail latency, allocations, CPU, database contention, queue growth, and recovery time under sustained load.
- [ ] Profile runtime and persistence bottlenecks, then verify changes against the same workloads.
- [ ] Add regression checks for the critical paths once stable baselines are available.

Published performance results should state their workload and environment. Flamme does not currently promise a universal throughput figure.

## Priority 2: strengthen distributed process execution

Make process behavior predictable across worker concurrency, dependency failures, and replica lifecycle changes.

- [ ] Validate workflow admission, trigger delivery, bookmark resumption, and competing workers under concurrency.
- [ ] Exercise process restarts, interrupted execution, dependency outages, and recovery with durable PostgreSQL state.
- [ ] Document delivery, retry, ordering, and idempotency requirements at orchestration boundaries.
- [ ] Verify coordination, tenant isolation, and workflow-state consistency across multiple replicas.
- [ ] Improve diagnostics for stalled processes, queue backlogs, retries, and recovery so operators can identify causes under load.

## Priority 3: make Kubernetes the primary operational path

Kubernetes guides deployment design and operational validation. The current container and health-check foundations are starting points; a turnkey production deployment is still roadmap work.

- [ ] Document reference Kubernetes deployment patterns for API hosts and workflow workers, including PostgreSQL connectivity and shared coordination dependencies.
- [ ] Validate readiness, traffic removal, graceful drain, termination, and recovery during rolling updates and pod replacement.
- [ ] Define resource sizing and scaling guidance using measured workloads, including limits imposed by database capacity and contention.
- [ ] Document configuration, secret injection, telemetry collection, and operational troubleshooting.
- [ ] Establish PostgreSQL migration, backup, restore, and disaster-recovery procedures for orchestrated processes.

Use the existing [Kubernetes probe guidance](doc/wiki/health-checks.md#kubernetes-recommendation) when configuring liveness and readiness.

## Persistence scope

Keeping the current database scope narrow reduces the maintenance and validation matrix while runtime and performance work progresses.

| Backend | Status | Intended role |
| --- | --- | --- |
| PostgreSQL | Supported durable database | Current production persistence focus through EF Core. |
| In-memory stores | Available for development and tests | Ephemeral, process-local state. |
| MySQL | Future support planned; not bundled | Broader database choice after the current priorities. |
| SQLite | Future support planned; not bundled | A future persistence option requiring its own validation. |
| Cassandra | Future support planned; not bundled | Explore persistence for high-load scenarios, with suitability established through implementation and measurement. |

No other database is currently supported for durable workflow persistence. Future providers require explicit compatibility, migration, recovery, and concurrency validation; this roadmap does not imply that removed provider packages can be used today.

The [migration guide](doc/postgres-inmemory-migration.md) describes removed providers and current configuration. It also records storage boundaries such as the absence of a bundled PostgreSQL structured-log backend.

## UI and authoring scope

The orchestration backend, APIs, performance, and operational reliability take priority. A visual designer and broader UI work are secondary and have no delivery commitment in this roadmap.

C# and JSON remain the main documented authoring paths. [Elsa Studio](https://github.com/elsa-workflows/elsa-studio) is a separate upstream project; its roadmap and releases do not define Flamme commitments.

## Contributing to these priorities

Proposals should explain the production scenario, expected behavior, and how the result can be verified. Performance changes should include reproducible measurements; distributed changes should include the relevant concurrency or failure scenario.

See [CONTRIBUTING.md](CONTRIBUTING.md) for the development workflow and open proposals in [Flamme issues](https://github.com/DenDeline/flamme/issues).
