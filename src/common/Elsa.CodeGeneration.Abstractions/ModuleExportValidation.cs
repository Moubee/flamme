namespace Elsa.CodeGeneration;

internal static class ModuleExportValidation
{
    internal static void RequireId(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || !string.Equals(value, value.Trim(), StringComparison.Ordinal))
            throw new ArgumentException("An identifier must be nonblank and must not contain leading or trailing whitespace.", parameterName);
    }

    internal static void RequirePositive(int value, string parameterName)
    {
        if (value <= 0)
            throw new ArgumentOutOfRangeException(parameterName, value, "A version must be a positive integer.");
    }

    internal static IReadOnlyList<string> SnapshotCapabilities(IEnumerable<string>? capabilities, string parameterName)
    {
        var snapshot = capabilities?.ToArray() ?? [];
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var capability in snapshot)
        {
            RequireId(capability, parameterName);
            if (!identities.Add(capability))
                throw new ArgumentException($"Duplicate capability '{capability}'.", parameterName);
        }
        return Array.AsReadOnly(snapshot);
    }
}