using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Elsa.CodeGeneration.Compiler;

internal static class CompilerDiagnostics
{
    private static readonly DiagnosticDescriptor Host = Descriptor("ELSAGEN001", "Declare the Elsa host contract");
    private static readonly DiagnosticDescriptor Entry = Descriptor("ELSAGEN002", "Declare the selected host bootstrap entry");
    private static readonly DiagnosticDescriptor Hook = Descriptor("ELSAGEN003", "Declare the selected host hook");
    private static readonly DiagnosticDescriptor Manual = Descriptor("ELSAGEN004", "Select the matching manual bootstrap hook");

    internal static readonly DiagnosticDescriptor DirectConstruction = new("ELSAGEN005", "Use a module creation entry point",
        "Creating Module here bypasses the fixed feature graph. Use services.CreateModule(), or select ElsaHostBootstrap=Manual for an authored catalog.",
        "Elsa.CodeGeneration", DiagnosticSeverity.Error, true);
    internal static readonly DiagnosticDescriptor IndirectCreation = new("ELSAGEN006", "Call the module creation entry point directly",
        "The {0} method group bypasses compiled feature registration. Invoke the method in a lambda or select ElsaHostBootstrap=Manual for an authored catalog.",
        "Elsa.CodeGeneration", DiagnosticSeverity.Error, true);

    internal static readonly DiagnosticDescriptor InaccessibleRequest = new("ELSAGEN010",
        "Provide an accessible binding for the requested type",
        "'{0}' requests '{1}', which cannot be bound by the generated Features helper: {2}",
        "Elsa.CodeGeneration", DiagnosticSeverity.Error, isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor InvalidPlan = Descriptor("ELSAGEN011", "Declare a valid fixed feature graph");
    internal static readonly DiagnosticDescriptor RuntimeSelection = Descriptor("ELSAGEN012", "Select the feature graph at build time");

    private static readonly DiagnosticDescriptor AuthoredGeneratedBootstrap = Descriptor("ELSAGEN013", "Migrate the authored bootstrap to fixed feature composition");

    internal static readonly DiagnosticDescriptor MutableComposition = new("ELSAGEN014", "Use fixed feature composition",
        "'{0}' selects a mutable catalog in a generated host. Use the ordinary fixed module creation overload or select ElsaHostBootstrap=Manual for an authored catalog.",
        "Elsa.CodeGeneration", DiagnosticSeverity.Error, true);

    internal static ImmutableArray<DiagnosticDescriptor> All => ImmutableArray.Create(Host, Entry, Hook, Manual, AuthoredGeneratedBootstrap);
    internal static DiagnosticDescriptor For(HostFailure failure) => failure switch
    {
        HostFailure.Entry => Entry,
        HostFailure.Hook => Hook,
        HostFailure.ManualSelection => Manual,
        HostFailure.AuthoredGeneratedBootstrap => AuthoredGeneratedBootstrap,
        _ => Host
    };

    private static DiagnosticDescriptor Descriptor(string id, string title) => new(id, title, "{0}", "Elsa.CodeGeneration", DiagnosticSeverity.Error, isEnabledByDefault: true,
        description: "Elsa host bootstrap selection is explicit at compile time. Generated and manual hosts use the same typed runtime contracts.");
}