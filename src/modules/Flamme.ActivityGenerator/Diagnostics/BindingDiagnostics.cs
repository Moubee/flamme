using Microsoft.CodeAnalysis;
using static Flamme.ActivityGenerator.Helpers.ActivitySymbols;

namespace Flamme.ActivityGenerator.Diagnostics;

internal static class BindingDiagnostics
{
    internal static readonly DiagnosticDescriptor InvalidBinding = new("FLAMME4501", "Activity binding cannot be generated",
        "Module '{0}', activity '{1}' is missing binding '{2}'. {3}.", "Flamme", DiagnosticSeverity.Error, true,
        customTags: [WellKnownDiagnosticTags.NotConfigurable]);

    internal static void Report(Action<Diagnostic> report, Compilation compilation, INamedTypeSymbol activity,
        string binding, string message, Location[]? sites = null, Location? request = null)
    {
        var primary = sites?.FirstOrDefault() ?? activity.Locations.FirstOrDefault(location => location.IsInSource) ?? request;
        report(Diagnostic.Create(InvalidBinding, primary, sites?.Skip(1), properties: null,
            messageArgs: [compilation.AssemblyName, TypeName(activity), binding, message.TrimEnd('.')]));
    }
}
