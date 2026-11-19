using Flamme.Identity.Contracts;
using Flamme.Identity.Entities;
using Flamme.Identity.Models;

namespace Flamme.Persistence.EFCore.Modules.Identity;

/// <summary>
/// An EF Core implementation of <see cref="IApplicationStore"/>.
/// </summary>
public class EFCoreApplicationStore : IApplicationStore
{
    private readonly EntityStore<IdentityFlammeDbContext, Application> _applicationStore;

    /// <summary>
    /// Initializes a new instance of <see cref="EFCoreApplicationStore"/>.
    /// </summary>
    public EFCoreApplicationStore(EntityStore<IdentityFlammeDbContext, Application> applicationStore)
    {
        _applicationStore = applicationStore;
    }

    /// <inheritdoc />
    public async Task SaveAsync(Application application, CancellationToken cancellationToken = default)
    {
        await _applicationStore.SaveAsync(application, cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(ApplicationFilter filter, CancellationToken cancellationToken = default)
    {
        await _applicationStore.DeleteWhereAsync(query => Filter(query, filter), cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Application?> FindAsync(ApplicationFilter filter, CancellationToken cancellationToken = default)
    {
        return await _applicationStore.FindAsync(query => Filter(query, filter), cancellationToken);
    }
    
    private static IQueryable<Application> Filter(IQueryable<Application> query, ApplicationFilter filter) => filter.Apply(query);
}