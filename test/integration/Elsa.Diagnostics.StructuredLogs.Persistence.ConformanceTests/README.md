# Structured-log store conformance tests

This suite runs the structured-log query contract against the bounded in-memory
store. It covers filters, paging, ordering, projection, and dropped-event counts.

Run it with:

```sh
dotnet test --project test/integration/Elsa.Diagnostics.StructuredLogs.Persistence.ConformanceTests/Elsa.Diagnostics.StructuredLogs.Persistence.ConformanceTests.csproj
```

No database service is required. SQLite persistence and its conformance fixture
have been removed. There is no bundled PostgreSQL structured-log provider; the
shared relational abstractions have their own unit tests.
