using System.Globalization;

namespace Flamme.ActivityGenerator;

// Assembly-specific namespaces keep generated types distinct for friend assemblies.
internal static class ActivityGeneratedNames
{
    internal const string Namespace = "Flamme.Workflows.Generated";
    internal const string ModuleName = "ActivityModule";

    internal static string ForProducer(string assemblyName)
    {
        var hash = 14695981039346656037UL;
        foreach (var character in assemblyName)
            hash = unchecked((hash ^ character) * 1099511628211UL);

        return Namespace + ".Producer_" + hash.ToString("x16", CultureInfo.InvariantCulture);
    }
}
