using System.Globalization;
using Flamme.ActivityGenerator.Helpers;
using Microsoft.CodeAnalysis;

namespace Flamme.ActivityGenerator.CodeGeneration;

// Assembly-specific namespaces keep generated types distinct for friend assemblies.
internal static class ActivityGeneratedNames
{
    internal const string Namespace = "Flamme.Workflows.Generated";
    internal const string ModuleName = "ActivityModule";

    internal static string ForProducer(string assemblyName) => Namespace + ".Producer_" + Hash(assemblyName);

    internal static string BridgeName(INamedTypeSymbol owner) =>
        "ActivityCallbacks_" + Hash(owner.ContainingAssembly.Name + ":" + ActivitySymbols.MetadataName(owner.OriginalDefinition));

    private static string Hash(string name)
    {
        var hash = 14695981039346656037UL;
        foreach (var character in name)
            hash = unchecked((hash ^ character) * 1099511628211UL);

        return hash.ToString("x16", CultureInfo.InvariantCulture);
    }
}
