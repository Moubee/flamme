# Flamme project direction

Flamme is an independent fork of [Elsa Workflows](https://github.com/elsa-workflows/elsa-core),
based on [Elsa 3.9](https://github.com/elsa-workflows/elsa-core/releases/tag/3.9.0).
The maintainer selected 3.9 as the most stable baseline for this project's needs.
Flamme builds on that foundation with a narrower purpose: process orchestration
for high-load, distributed production systems.

## Why fork and rename?

The fork makes room for independent priorities around throughput, latency,
resource use, and reliable operation across multiple runtime instances.
Reducing the database and hosting scope lets development and validation focus
on those priorities. The Flamme name identifies this direction and its
maintenance separately from upstream Elsa.

Elsa remains the original project, and its contributors provided the workflow
engine on which Flamme is built. The fork retains the [MIT license](../LICENSE)
and upstream attribution. The concise origin statement in the
[Valkey README](https://github.com/valkey-io/valkey#readme) informed how this fork
is introduced; Flamme's reasons are its own engineering and product priorities.

The product name is Flamme. Existing `Elsa.*` project names, package identifiers,
namespaces, configuration keys, and the `Elsa.slnx` solution still describe the
current source tree. Renaming the project does not itself migrate these APIs or
establish compatibility with future upstream releases.

## Scope and priorities

| Area | Direction |
| --- | --- |
| Purpose | Orchestrate business processes and service interactions through short- and long-running workflows. |
| Performance | Prioritize high-load workloads, with representative benchmarks and regression evidence guiding changes. |
| Distributed execution | Focus on coordination, recovery, concurrency, and predictable operation across runtime instances. |
| Hosting | Treat Kubernetes as the primary production hosting target. Local .NET and Docker workflows remain useful for development. |
| Durable persistence | Support PostgreSQL only for now, reducing provider maintenance and the validation matrix. |
| In-memory stores | Retain process-local stores for development and tests; their data is neither durable nor shared across nodes. |
| User interface | Keep UI and visual-designer work secondary to the engine, APIs, and operational behavior. |

High-load performance and production reliability are development priorities.
They are not throughput guarantees or a claim that every distributed deployment
scenario is already validated.

## Persistence policy

The current durable database implementation uses PostgreSQL through EF Core.
Other database providers and the experimental persistence vNext system have
already been removed; the [migration guide](postgres-inmemory-migration.md)
describes the package, configuration, and data migration implications.

MySQL, SQLite, and Cassandra support is planned for the future. Cassandra is
intended for high-load scenarios. None of these providers is currently bundled
or supported, and there is no committed delivery date. Future providers need
their own design and operational validation before joining the supported set.

This database policy covers engine and module persistence. It does not mean
that a process cannot call an external database or other service as part of its
work. Operational logs and telemetry are collected and stored by the host's
external observability pipeline. Elsa supplies workflow activity sources and
meters; official OpenTelemetry packages handle export. See the
[telemetry configuration and migration guide](wiki/opentelemetry-workflows.md).

## Kubernetes and operations

Kubernetes is the primary hosting scenario to guide deployment and runtime
work. Existing foundations include container builds, separate liveness and
readiness endpoints, and distributed runtime services. See the
[health-check guide](wiki/health-checks.md) for the implemented probes.

The reference server is a development host. Its development configuration allows
local file-system locks; a multi-node deployment needs a lock provider shared
across nodes. A database connection and additional replicas alone do not make
the sample a validated distributed production deployment. Deployment guidance,
failure testing, resource tuning, and rolling-update behavior are roadmap work.

## Research basis and next work

The direction above is grounded in the current repository:

- [Build SDK selection](../global.json) pins .NET SDK 11.0.100 with patch roll-forward.
- [Library targets](../src/Directory.Build.props) and
  [reference host targets](../src/apps/Directory.Build.props) are .NET 10 and .NET 11.
- [Reference server configuration](../src/apps/Elsa.Server.Web/Program.cs) wires
  PostgreSQL persistence, distributed runtime behavior, and readiness checks.
- [Persistence migration guidance](postgres-inmemory-migration.md) records the
  providers removed from the supported scope and remaining module limitations.

The high-level work areas are performance measurement, distributed correctness,
Kubernetes operations, and PostgreSQL hardening. The [Flamme roadmap](../ROADMAP.md)
tracks these priorities. The [README](../README.md) is the project introduction,
and [contributor guidance](../CONTRIBUTING.md) describes the development workflow.
