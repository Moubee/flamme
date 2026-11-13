# Quickstart: Structured Log Storage

Structured logs use a bounded in-memory store. No database package or migration is required.

```csharp
services.AddElsa(elsa => elsa.UseStructuredLogs());

app.UseStructuredLogs();
```

Recent history is process-local and is lost when the host stops. In clustered deployments, each node has its own history. Existing REST and SignalR contracts continue to expose recent records and live events.

The legacy durable provider has been removed. The shared relational package contains extension contracts and helpers, but it does not supply a concrete database backend. See the [storage upgrade guide](../../doc/postgres-inmemory-migration.md).

Run the core structured-log checks with:

```bash
dotnet test --project test/unit/Elsa.Diagnostics.StructuredLogs.UnitTests/Elsa.Diagnostics.StructuredLogs.UnitTests.csproj
dotnet test --project test/integration/Elsa.Diagnostics.StructuredLogs.IntegrationTests/Elsa.Diagnostics.StructuredLogs.IntegrationTests.csproj
```
