using Flamme.Workflows.Models;

namespace Flamme.Workflows;

/// <summary>Checks a proposed activity binding before registration or storage.</summary>
public interface IActivityDescriptorValidator
{
    /// <summary>Returns the reason this binding is invalid, or null when it can be used.</summary>
    string? GetValidationError(ActivityDescriptor descriptor);
}
