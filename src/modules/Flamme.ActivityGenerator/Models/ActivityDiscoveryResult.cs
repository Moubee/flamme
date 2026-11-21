using Microsoft.CodeAnalysis;

namespace Flamme.ActivityGenerator.Models;

internal sealed record ActivityDiscoveryResult(
    Compilation Compilation,
    Dictionary<INamedTypeSymbol, bool> Descriptors,
    Dictionary<INamedTypeSymbol, Location> ExplicitSites,
    HashSet<INamedTypeSymbol> Inferred,
    HashSet<INamedTypeSymbol> BridgeOwners,
    Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>> ExplicitContexts);
