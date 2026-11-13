# Diagnostics Structured Logs

`Elsa.Diagnostics.StructuredLogs` captures semantic `ILogger` events from an Elsa host, redacts sensitive data, keeps a recent queryable buffer, exposes REST endpoints, and streams live events to Studio over SignalR.

Start in [src/modules/Elsa.Diagnostics.StructuredLogs](../../src/modules/Elsa.Diagnostics.StructuredLogs).

## Scope

This module captures structured `ILogger` records only. It does not capture raw stdout/stderr console streams, traces, metrics, or OpenTelemetry spans. Raw console output is covered by the sibling [Elsa.Diagnostics.ConsoleLogs](../../src/modules/Elsa.Diagnostics.ConsoleLogs) module — see [Diagnostics Console Logs](diagnostics-console-logs.md).

## Feature Wiring

[StructuredLogsFeature](../../src/modules/Elsa.Diagnostics.StructuredLogs/Features/StructuredLogsFeature.cs):

- registers FastEndpoints assembly
- calls `AddStructuredLogsServices`
- adds FastEndpoints from the module

[AddStructuredLogsServices](../../src/modules/Elsa.Diagnostics.StructuredLogs/Extensions/ServiceCollectionExtensions.cs) registers:

- SignalR
- `StructuredLogsOptions`
- source registry
- redactor
- in-memory store
- in-memory live feed
- default provider facade
- subscription manager
- `StructuredLogLoggerProvider` as an `ILoggerProvider`

## Core Contracts

| Contract | Purpose |
| --- | --- |
| [IStructuredLogProvider](../../src/modules/Elsa.Diagnostics.StructuredLogs/Contracts/IStructuredLogProvider.cs) | REST/SignalR facade used by endpoints and clients. |
| [IStructuredLogStore](../../src/modules/Elsa.Diagnostics.StructuredLogs/Contracts/IStructuredLogStore.cs) | Queryable storage abstraction. |
| [IStructuredLogLiveFeed](../../src/modules/Elsa.Diagnostics.StructuredLogs/Contracts/IStructuredLogLiveFeed.cs) | Live event publication/subscription abstraction. |
| [IStructuredLogSink](../../src/modules/Elsa.Diagnostics.StructuredLogs/Contracts/IStructuredLogSink.cs) | Event ingestion boundary. |
| [IStructuredLogRedactor](../../src/modules/Elsa.Diagnostics.StructuredLogs/Contracts/IStructuredLogRedactor.cs) | Redacts properties and text before storage/live delivery. |
| [IStructuredLogSourceRegistry](../../src/modules/Elsa.Diagnostics.StructuredLogs/Contracts/IStructuredLogSourceRegistry.cs) | Tracks source metadata and health. |
| [IStructuredLogStorageDiagnostics](../../src/modules/Elsa.Diagnostics.StructuredLogs/Contracts/IStructuredLogStorageDiagnostics.cs) | Provider-neutral diagnostics such as dropped durable writes. |

## Event Flow

```mermaid
sequenceDiagram
    participant App as App ILogger
    participant Provider as StructuredLogLoggerProvider
    participant Redactor as IStructuredLogRedactor
    participant Store as IStructuredLogStore
    participant Feed as IStructuredLogLiveFeed
    participant Hub as StructuredLogsHub
    participant Studio as Studio

    App->>Provider: Log event + scopes
    Provider->>Redactor: redact event
    Redactor->>Store: append event
    Redactor->>Feed: publish event
    Feed->>Hub: live event
    Hub->>Studio: SignalR stream
    Studio->>Store: recent query through REST
```

## In-Memory Provider

The default provider keeps recent logs in process:

- [InMemoryStructuredLogStore](../../src/modules/Elsa.Diagnostics.StructuredLogs/Providers/InMemory/InMemoryStructuredLogStore.cs)
- [InMemoryStructuredLogLiveFeed](../../src/modules/Elsa.Diagnostics.StructuredLogs/Providers/InMemory/InMemoryStructuredLogLiveFeed.cs)
- [RingBuffer](../../src/modules/Elsa.Diagnostics.StructuredLogs/Providers/InMemory/RingBuffer.cs)

This is bounded and process-local. In clustered deployments, each node has its own source identity and in-memory history and history is lost on process restart. No durable backend is bundled.

## REST And SignalR Surface

REST endpoints:

- `GET|POST /elsa/api/diagnostics/structured-logs/recent`
- `GET /elsa/api/diagnostics/structured-logs/sources`
- `GET /elsa/api/diagnostics/structured-logs/storage`

Endpoint code is under [Endpoints/StructuredLogs](../../src/modules/Elsa.Diagnostics.StructuredLogs/Endpoints/StructuredLogs).

SignalR:

- Hub: [StructuredLogsHub](../../src/modules/Elsa.Diagnostics.StructuredLogs/RealTime/StructuredLogsHub.cs)
- Client contract: [IStructuredLogsClient](../../src/modules/Elsa.Diagnostics.StructuredLogs/RealTime/IStructuredLogsClient.cs)
- Mapping: [MapStructuredLogsHub](../../src/modules/Elsa.Diagnostics.StructuredLogs/Extensions/EndpointRouteBuilderExtensions.cs)
- App extension: [UseStructuredLogs](../../src/modules/Elsa.Diagnostics.StructuredLogs/Extensions/ApplicationBuilderExtensions.cs)

The README states the hub is mapped at `/elsa/hubs/diagnostics/structured-logs`.

## Authorization

The endpoints require `read:diagnostics:structured-logs`, defined in [StructuredLogsResourcePermissions](../../src/modules/Elsa.Diagnostics.StructuredLogs/Permissions/StructuredLogsResourcePermissions.cs). The SignalR hub requires an authenticated user.

## Redaction

Events pass through `IStructuredLogRedactor` before buffering or streaming. Configuration lives in [StructuredLogsOptions](../../src/modules/Elsa.Diagnostics.StructuredLogs/Options/StructuredLogsOptions.cs). Extend sensitive property names and text patterns there.

## Storage Support

The bundled store is bounded and in-memory. The legacy durable provider has been removed; see the [upgrade guide](../postgres-inmemory-migration.md). Configure an external logging pipeline when logs must survive process restarts.

## Relational Persistence Design

[AddRelationalStructuredLogPersistence](../../src/modules/Elsa.Diagnostics.StructuredLogs.Persistence.Relational/Extensions/RelationalStructuredLogsServiceCollectionExtensions.cs) registers:

- `RelationalStructuredLogMapper`
- `RelationalStructuredLogSqlBuilder`
- `RelationalStructuredLogStore`
- `StructuredLogWriteBuffer`
- `StructuredLogRetentionService`
- `IStructuredLogStore` as the write buffer
- `IStructuredLogWriteBuffer`
- `IStructuredLogStorageDiagnostics`
- hosted service for the write buffer

The write buffer uses a bounded queue. If the queue is full, newest events are dropped and the dropped-write count is reported through storage diagnostics.

Recent-log queries honor `StructuredLogsOptions.MaxRecentLogQuerySize` for both the default `Take` and the upper clamp, matching the in-memory store. Query order is `Timestamp`, `ReceivedAt`, `SourceId`, `Sequence`, then `Id`. `ListSources` prefers the in-process source registry for identity metadata and heartbeat/`Stale` status, and uses persisted `SourceId` rows only as a fallback for sources this process has not seen.

## Relational Extension Boundary

The relational package retains shared connection, dialect, migration, write-buffer, and retention contracts. It does not register a concrete database provider by itself. PostgreSQL is the only supported durable database for any host-provided implementation.

## Tests

Relevant tests:

- [test/unit/Elsa.Diagnostics.StructuredLogs.UnitTests](../../test/unit/Elsa.Diagnostics.StructuredLogs.UnitTests)
- [test/integration/Elsa.Diagnostics.StructuredLogs.IntegrationTests](../../test/integration/Elsa.Diagnostics.StructuredLogs.IntegrationTests)
- [test/unit/Elsa.Diagnostics.StructuredLogs.Persistence.Relational.UnitTests](../../test/unit/Elsa.Diagnostics.StructuredLogs.Persistence.Relational.UnitTests)

Run targeted structured-log tests before broader builds when touching this area.
