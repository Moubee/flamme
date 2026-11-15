# Upgrade to PostgreSQL and in-memory persistence

PostgreSQL is Flamme's only supported durable database. Core in-memory stores remain available for development and tests, with process-local state that is lost on restart. Narrowing the provider scope reduces maintenance and validation work while the project focuses on high-load, distributed process orchestration. This is a breaking package and configuration change; see [project direction](project-direction.md).

MySQL, SQLite, and Cassandra support is planned for the future, with Cassandra intended for high-load scenarios. None is currently bundled or supported, and no delivery date is committed. The removed packages below do not become supported through this plan.

## Removed packages and APIs

SQLite, SQL Server, MySQL, and Oracle EF Core provider packages have been removed for workflows, identity, tenants, labels, alterations, AI, external authentication, secrets, and User Tasks. Their provider extension methods, shell features, design-time factories, migrations, and provider-specific tests are also removed.

The entire vNext persistence system has been removed, including its PostgreSQL, relational, document, MongoDB, runtime-entity, Secrets, and User Tasks adapters. PostgreSQL remains supported through the existing EF Core packages.

Replace project/package references with the matching `*.Persistence.EFCore.PostgreSql` package. Remove legacy provider feature names, connection-string keys, and vNext registration calls from host configuration.

## Select the storage mode explicitly

Core features provide process-local in-memory stores for development and tests. Omitting database persistence registration selects those stores. Data in those stores is lost when the process stops and is not shared between nodes.

For durable workflow management and runtime persistence, reference `Elsa.Persistence.EFCore.PostgreSql` and supply a PostgreSQL connection string:

```csharp
using Elsa.Persistence.EFCore.Extensions;
using Elsa.Extensions;

var connectionString = configuration.GetConnectionString("PostgreSql")
    ?? throw new InvalidOperationException("Configure ConnectionStrings:PostgreSql.");

services.AddElsa(elsa =>
{
    elsa.UseWorkflowManagement(management =>
        management.UseEntityFrameworkCore(ef => ef.UsePostgreSql(connectionString)));
    elsa.UseWorkflowRuntime(runtime =>
        runtime.UseEntityFrameworkCore(ef => ef.UsePostgreSql(connectionString)));
});
```

Configure each additional module's PostgreSQL persistence feature separately. Shell hosts use the `PostgreSqlWorkflowPersistence`, `PostgreSqlIdentityPersistence`, `PostgreSqlExternalAuthenticationPersistence`, and corresponding module feature names. Keep connection credentials in environment variables or a secrets manager.

PostgreSQL migrations remain the schema authority for durable stores. Existing PostgreSQL deployments keep their provider migration history. Switching from another database or vNext storage requires a deliberate data transfer: this change does not supply an automatic cross-database importer. Back up the original store, prepare the PostgreSQL schema, transfer and validate application data, and verify workflow resumption before switching production traffic. Preserve ASP.NET Core Data Protection keys when moving encrypted secrets or other protected payloads.

## Structured logs

All custom structured-log, console-log, and OpenTelemetry diagnostics packages, including relational structured-log helpers, have been removed. Use the host's external logging and observability pipeline for operational telemetry. Remove the former diagnostics feature registrations and configuration; see the [telemetry migration guide](wiki/opentelemetry-workflows.md#upgrading-from-elsa-diagnostics-packages). Workflow execution records and retry history remain domain data.

## Tests

Removed backend and vNext tests are no longer part of the solution. In-memory suites cover provider-independent behavior. PostgreSQL suites cover durable behavior when their documented test-database prerequisites are configured. An in-memory run does not establish PostgreSQL migration, constraint, transaction, restart, or concurrency guarantees.

Shared PostgreSQL fixtures create isolated databases on the server selected by
`ELSA_TEST_POSTGRES`, or start a `postgres:17-alpine` Testcontainers instance when
that variable is unset. Docker is required for the automatic container path;
an explicitly configured server requires a role that can create databases. The
helper drops only the isolated databases it creates. User Tasks conformance runs
both supported backends by default; its optional supplied-database override is
described in the conformance suite README.

Older release notes, dated QA evidence, and security assessments describe their original revisions; their provider lists and test counts do not define current support.
