using Flamme.Features.Contracts;
using Flamme.Features.Models;

namespace Flamme.Features.Services;

/// <summary>
/// Implementation of <see cref="IInstalledFeatureProvider"/> that delegates to <see cref="IInstalledFeatureRegistry"/>.
/// </summary>
/// <remarks>
/// Features registered through a Flamme module are published in the installed feature registry.
/// </remarks>
public class InstalledFeatureProvider : IInstalledFeatureProvider
{
    private readonly IInstalledFeatureRegistry _registry;

    /// <summary>
    /// Initializes a new instance of the <see cref="InstalledFeatureProvider"/> class.
    /// </summary>
    public InstalledFeatureProvider(IInstalledFeatureRegistry registry)
    {
        _registry = registry;
    }

    /// <inheritdoc />
    public IEnumerable<FeatureDescriptor> List() => _registry.List();

    /// <inheritdoc />
    public FeatureDescriptor? Find(string fullName) => _registry.Find(fullName);
}
