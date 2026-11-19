using Flamme.Identity.Contracts;
using Flamme.Identity.Entities;
using Flamme.Identity.Models;

namespace Flamme.Identity.Providers;

/// <summary>
/// Represents an application provider that uses <see cref="IApplicationStore"/> to find applications.
/// </summary>
public class StoreBasedApplicationProvider : IApplicationProvider
{
    private readonly IApplicationStore _applicationStore;

    /// <summary>
    /// Initializes a new instance of the <see cref="StoreBasedApplicationProvider"/> class.
    /// </summary>
    public StoreBasedApplicationProvider(IApplicationStore applicationStore)
    {
        _applicationStore = applicationStore;
    }
    
    /// <inheritdoc />
    public async Task<Application?> FindAsync(ApplicationFilter filter, CancellationToken cancellationToken = default)
    {
        return await _applicationStore.FindAsync(filter, cancellationToken);
    }
}