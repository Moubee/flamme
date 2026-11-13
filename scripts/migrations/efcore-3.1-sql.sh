#!/usr/bin/env bash

repository_root="$(cd "$(dirname "$0")/../.." && pwd)"

# Define the modules to update
mods=("Alterations" "Runtime" "Management" "Identity")

# Define the list of providers
providers=("PostgreSql")

# Loop through each module
for module in "${mods[@]}"; do
    # Loop through each provider
    for provider in "${providers[@]}"; do
        providerPath="$repository_root/src/modules/Elsa.Persistence.EFCore.$provider"
        sqlPath="$providerPath/Migrations/$module/v3.1.sql"
    
        echo "Generating SQL for $provider..."
        echo "SQL path: $sqlPath"
        dotnet ef migrations script -p "$providerPath" -c "$module"ElsaDbContext -o "$sqlPath"
        
        # Make the file writable:
        chmod 777 "$sqlPath"
    done
done
