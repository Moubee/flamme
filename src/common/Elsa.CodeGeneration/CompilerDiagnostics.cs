using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Elsa.CodeGeneration.Compiler;

internal static class CompilerDiagnostics
{
    private static readonly DiagnosticDescriptor Host = Descriptor("ELSAGEN001", "Declare the Elsa host contract");
    private static readonly DiagnosticDescriptor Entry = Descriptor("ELSAGEN002", "Declare the selected host bootstrap entry");
    private static readonly DiagnosticDescriptor Hook = Descriptor("ELSAGEN003", "Declare the selected host hook");
    private static readonly DiagnosticDescriptor Manual = Descriptor("ELSAGEN004", "Select the matching manual bootstrap hook");

    internal static ImmutableArray<DiagnosticDescriptor> All => ImmutableArray.Create(Host, Entry, Hook, Manual);
    internal static DiagnosticDescriptor For(HostFailure failure) => failure switch
    {
        HostFailure.Entry => Entry,
        HostFailure.Hook => Hook,
        HostFailure.ManualSelection => Manual,
        _ => Host
    };

    private static DiagnosticDescriptor Descriptor(string id, string title) => new(id, title, "{0}", "Elsa.CodeGeneration", DiagnosticSeverity.Error, isEnabledByDefault: true,
        description: "Elsa host bootstrap selection is explicit at compile time. Generated and manual hosts use the same typed runtime contracts.");
}