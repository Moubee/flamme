using Microsoft.CodeAnalysis;

namespace Flamme.ActivityGenerator.Diagnostics;

internal sealed class BindingException(string binding, string message, params Location[] sites) : Exception(message)
{
    public string Binding { get; } = binding;
    public Location[] Sites { get; } = sites;
}
