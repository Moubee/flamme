using Flamme.Identity.Contracts;
using Flamme.Identity.Entities;
using Flamme.Identity.Models;

namespace Flamme.Persistence.EFCore.Modules.Identity;

/// <summary>
/// An EF Core implementation of <see cref="IUserStore"/>.
/// </summary>
public class EFCoreUserStore(EntityStore<IdentityFlammeDbContext, User> userStore) : IUserStore
{
    /// <inheritdoc />
    public async Task SaveAsync(User user, CancellationToken cancellationToken = default)
    {
        await userStore.SaveAsync(user, cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(UserFilter filter, CancellationToken cancellationToken = default)
    {
        await userStore.DeleteWhereAsync(query => Filter(query, filter), cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IEnumerable<User>> FindManyAsync(UserFilter filter, CancellationToken cancellationToken = default)
    {
        return await userStore.QueryAsync(query => Filter(query, filter), cancellationToken);
    }

    /// <inheritdoc />
    public async Task<User?> FindAsync(UserFilter filter, CancellationToken cancellationToken = default)
    {
        return await userStore.FindAsync(query => Filter(query, filter), cancellationToken);
    }
    
    private static IQueryable<User> Filter(IQueryable<User> query, UserFilter filter) => filter.Apply(query);
}