The `ef-migration-runtime-schema` package provides a runtime schema for the Entity Framework Core migrations. This schema is used to generate the migration scripts for the database.

https://www.nuget.org/packages/ef-migration-runtime-schema

The current generator at
`src/modules/Flamme.ExternalAuthentication.Persistence.EFCore/efcore-initial.sh`
explicitly selects `net10.0`, matching the EF Core 10 tooling in
`.config/dotnet-tools.json`. Restore local tools with `dotnet tool restore` before
generating migrations. Obsolete fixed-version generators have been removed.
