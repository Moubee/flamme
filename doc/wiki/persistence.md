# Persistence

Flamme uses replaceable stores. Core features provide in-memory stores, and PostgreSQL EF Core packages add durable persistence. Structured logs use a bounded in-memory store; the shared relational extension contracts have no bundled durable backend.

## Store Replacement Pattern

Feature classes expose store factories. Persistence features replace those factories during `Configure()`.

Example from [EFCoreWorkflowRuntimePersistenceFeature](../../src/modules/Flamme.Persistence.EFCore/Modules/Runtime/WorkflowRuntimePersistenceFeature.cs):

- replace `WorkflowRuntimeFeature.TriggerStore`
- replace `BookmarkStore`
- replace `BookmarkQueueStore`
- replace `WorkflowExecutionLogStore`
- replace `ActivityExecutionLogStore`
- replace key-value store

Management persistence follows the same idea for definition and instance stores.

## EF Core Shared Infrastructure

Shared EF Core infrastructure lives in [Flamme.Persistence.EFCore.Common](../../src/modules/Flamme.Persistence.EFCore.Common) and [Flamme.Persistence.EFCore](../../src/modules/Flamme.Persistence.EFCore).

Important base types:

- [PersistenceFeatureBase](../../src/modules/Flamme.Persistence.EFCore.Common/PersistenceFeatureBase.cs)
- [FlammeDbContextBase](../../src/modules/Flamme.Persistence.EFCore.Common/FlammeDbContextBase.cs)

`PersistenceFeatureBase` registers `IDbContextFactory<TDbContext>`, migration options, tenant-aware context factory decoration, and tenant model handlers.

## EF Core Module Slices

The shared EF Core module contains slices for:

- [Management](../../src/modules/Flamme.Persistence.EFCore/Modules/Management)
- [Runtime](../../src/modules/Flamme.Persistence.EFCore/Modules/Runtime)
- [Identity](../../src/modules/Flamme.Persistence.EFCore/Modules/Identity)
- [Tenants](../../src/modules/Flamme.Persistence.EFCore/Modules/Tenants)
- [Labels](../../src/modules/Flamme.Persistence.EFCore/Modules/Labels)
- [Alterations](../../src/modules/Flamme.Persistence.EFCore/Modules/Alterations)

Each slice has a DbContext, configurations, store implementations, and code-first feature classes.

## Provider Packages

Provider packages configure database-specific EF Core options and migrations:

- [Flamme.Persistence.EFCore.PostgreSql](../../src/modules/Flamme.Persistence.EFCore.PostgreSql)

Configure each required persistence slice through its code-first feature and PostgreSQL provider extension. Shell-based persistence feature classes have been removed; migrate their connection and migration settings to the host registrations below.

## Typical Host Configuration

To enable durable management and runtime persistence, reference `Flamme.Persistence.EFCore.PostgreSql` and configure the connection explicitly:

```csharp
flamme.UseWorkflowManagement(management =>
{
    management.UseEntityFrameworkCore(ef => ef.UsePostgreSql(connectionString));
});

flamme.UseWorkflowRuntime(runtime =>
{
    runtime.UseEntityFrameworkCore(ef => ef.UsePostgreSql(connectionString));
});
```

The reference server uses this PostgreSQL configuration through `ConnectionStrings:PostgreSql`; see [src/apps/Flamme.Server.Web/Program.cs](../../src/apps/Flamme.Server.Web/Program.cs). Custom hosts can omit the EF Core registrations to keep core in-memory stores.

## Migrations

EF Core migrations are controlled by feature options such as `RunMigrations`. The base persistence feature registers startup tasks that run migrations when enabled.

Migration-related files:

- [MigrationOptions](../../src/modules/Flamme.Persistence.EFCore.Common/MigrationOptions.cs)
- [RunMigrationsStartupTask](../../src/modules/Flamme.Persistence.EFCore.Common/RunMigrationsStartupTask.cs)
- [scripts/migrations/README.md](../../scripts/migrations/README.md)

When adding an entity or changing persisted shape, update the PostgreSQL migrations and test their behavior against PostgreSQL.

## Tenant Awareness

EF Core persistence decorates `IDbContextFactory<TDbContext>` with `TenantAwareDbContextFactory<TDbContext>` and registers model/saving handlers:

- `ApplyTenantId`
- `SetTenantIdFilter`

Default Memory stores must apply the same admission rule through `TenantVisibility` (ambient match, `*`, null-on-default-tenant) and stamp the ambient tenant on save when unset.

`SerializedKeyValuePair` is keyed by `Id` (= `Key`) alone. `TenantId` is a filter/index, not part of the primary key, so two tenants cannot persist the same key under EF (PK collision). That is intentional for cluster-wide keys (heartbeat, quiescence): callers that need tenant-scoped names must encode the tenant into the key. A composite `(TenantId, Key)` identity is out of scope.

Memory stamps the ambient tenant on save, filters Find/FindMany with `TenantVisibility`, and deletes through `IsVisible` (a tenant can delete its own rows and `*` rows, but not another named tenant's row). Save also mirrors the EF PK collision with `CanReplaceOwnedRow`: a named tenant cannot take over another tenant's key or a `*` key (`InvalidOperationException`); an accepted update keeps the existing `TenantId`. The ownership check is skipped when no `ITenantAccessor` is registered.

Tenant conventions are documented in ADRs:

- [ADR 0008: Empty String As Default Tenant ID](../adr/0008-empty-string-as-default-tenant-id.md)
- [ADR 0009: Asterisk Sentinel Value For Tenant-Agnostic Entities](../adr/0009-asterisk-sentinel-value-for-tenant-agnostic-entities.md)

## Operational Log Storage

Operational logs and telemetry are stored by the host's external logging and observability pipeline. Flamme's custom structured-log, console-log, and OpenTelemetry storage packages have been removed; see the [migration guide](opentelemetry-workflows.md#upgrading-from-elsa-diagnostics-packages). Workflow execution journals and persisted activity records remain domain data.

## Adding A Store

When adding a new store implementation:

1. Identify the feature contract that owns the store.
2. Keep the core module provider-neutral.
3. Add the concrete store in the persistence/provider module.
4. Replace the feature's store factory in the persistence feature.
5. Add unit tests for store-specific query behavior if logic is nontrivial.
6. Add integration tests for provider behavior, migrations, and multi-target concerns where practical.

## Supported Storage Policy

PostgreSQL and in-memory stores are the supported backends. See the [upgrade guide](../postgres-inmemory-migration.md) for package removals and configuration changes.

## Persistence Risk Checklist

- Does the change affect multiple target frameworks?
- Does it need provider-specific migrations?
- Does it preserve tenant filtering?
- Does it update both definition and instance stores if both shapes changed?
- Does it require API/client DTO updates?
- Does it alter runtime recovery, bookmark, or trigger semantics?
- Does it need retention, cleanup, or migration documentation?
