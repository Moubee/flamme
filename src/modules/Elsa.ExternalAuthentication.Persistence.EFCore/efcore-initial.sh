#!/usr/bin/env bash

# Creates the initial migration for ExternalAuthenticationElsaDbContext for PostgreSQL.
# Run from this directory so the relative provider paths resolve.
#
# Use the .NET 10 target with the EF Core 10 tooling selected by .config/dotnet-tools.json.
# ef-migration-runtime-schema rewrites the generated migration to take IElsaDbContextSchema, which is what
# keeps the migration usable with a non-default schema name.

migrationName="${1:-Initial}"

# Define the list of providers
providers=("PostgreSql")

for provider in "${providers[@]}"; do
    providerPath="../Elsa.ExternalAuthentication.Persistence.EFCore.$provider"
    startupProject="$providerPath/Elsa.ExternalAuthentication.Persistence.EFCore.$provider.csproj"
    migrationsPath="Migrations/ExternalAuthentication"

    echo "Creating $migrationName migration for $provider..."
    echo "Provider path: ${providerPath:?}"
    echo "Startup project: $startupProject"
    echo "Migrations path: $migrationsPath"
    ef-migration-runtime-schema --interface Elsa.Persistence.EFCore.IElsaDbContextSchema --efOptions "migrations add ""$migrationName"" -c ExternalAuthenticationElsaDbContext -p ""$providerPath"" -o ""$migrationsPath"" --startup-project ""$startupProject"" --framework net10.0"
done
