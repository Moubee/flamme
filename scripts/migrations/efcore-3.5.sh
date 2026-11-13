#!/usr/bin/env bash

repository_root="$(cd "$(dirname "$0")/../.." && pwd)"

# Define the modules to update
mods=("Runtime")

# Define the list of providers
providers=("PostgreSql")

# Loop through each module
for module in "${mods[@]}"; do
    # Loop through each provider
    for provider in "${providers[@]}"; do
        providerPath="$repository_root/src/modules/Elsa.Persistence.EFCore.$provider"
        migrationsPath="Migrations/$module"
    
        echo "Updating migrations for $provider..."
        echo "Provider path: ${providerPath:?}/${migrationsPath}"
        echo "Migrations path: $migrationsPath"
        ef-migration-runtime-schema --interface Elsa.Persistence.EFCore.IElsaDbContextSchema --efOptions "migrations add V3_5 -c ""$module""ElsaDbContext -p ""$providerPath""  -o ""$migrationsPath"""
    done
done
