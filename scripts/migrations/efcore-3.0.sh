#!/usr/bin/env zsh

repository_root="$(cd "$(dirname "$0")/../.." && pwd)"

# Define the modules to update
mods=("Alterations" "Runtime" "Management" "Identity" "Labels")

# Define the list of providers
providers=("PostgreSql")

# Connection strings for each provider
typeset -A connStrings
connStrings=(
    PostgreSql ""
)

# Loop through each module
for module in "${mods[@]}"; do
    # Loop through each provider
    for provider in "${providers[@]}"; do
        providerPath="$repository_root/src/modules/Elsa.Persistence.EFCore.$provider"
        migrationsPath="Migrations/$module"
    
        echo "Updating migrations for $provider..."
        echo "Provider path: ${providerPath:?}/${migrationsPath}"
        echo "Migrations path: $migrationsPath"
        echo "Connection string: ${connStrings[$provider]}"
    
        # 1. Delete the existing migrations folder
        rm -rf "${providerPath:?}/${migrationsPath}"
    
        # 2. Run the migrations command
        dotnet ef migrations add Initial -c "$module"ElsaDbContext -p "$providerPath"  -o "$migrationsPath" -- --connectionString "${connStrings[$provider]}"
    done
done
