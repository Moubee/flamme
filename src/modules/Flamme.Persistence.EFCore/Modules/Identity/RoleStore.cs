using Flamme.Identity.Contracts;
using Flamme.Identity.Entities;
using Flamme.Identity.Models;
using Open.Linq.AsyncExtensions;

namespace Flamme.Persistence.EFCore.Modules.Identity;

/// <summary>
/// An EF Core implementation of <see cref="IRoleStore"/>.
/// </summary>
public class EFCoreRoleStore : IRoleStore, IRoleStoreWithAtomicDelete
{
    private readonly EntityStore<IdentityFlammeDbContext, Role> _applicationStore;

    /// <summary>
    /// Initializes a new instance of <see cref="EFCoreRoleStore"/>.
    /// </summary>
    public EFCoreRoleStore(EntityStore<IdentityFlammeDbContext, Role> applicationStore)
    {
        _applicationStore = applicationStore;
    }

    /// <inheritdoc />
    public async Task SaveAsync(Role application, CancellationToken cancellationToken = default)
    {
        await _applicationStore.SaveAsync(application, cancellationToken);
    }

    /// <inheritdoc />
    public async Task AddAsync(Role role, CancellationToken cancellationToken = default)
    {
        await _applicationStore.AddAsync(role, cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(RoleFilter filter, CancellationToken cancellationToken = default)
    {
        await _applicationStore.DeleteWhereAsync(query => Filter(query, filter), cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The delete is issued as a single <c>DELETE ... WHERE</c> statement and the affected-row count it returns is
    /// the database's own verdict on which caller removed the row, so two concurrent callers cannot both observe
    /// <see langword="true"/>.
    /// </remarks>
    public async Task<bool> TryDeleteAsync(string roleId, CancellationToken cancellationToken = default)
    {
        return await _applicationStore.DeleteWhereAsync(query => Filter(query, new RoleFilter { Id = roleId }), cancellationToken) > 0;
    }

    /// <inheritdoc />
    public async Task<Role?> FindAsync(RoleFilter filter, CancellationToken cancellationToken = default)
    {
        return await _applicationStore.FindAsync(query => Filter(query, filter), cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IEnumerable<Role>> FindManyAsync(RoleFilter filter, CancellationToken cancellationToken = default)
    {
        return await _applicationStore.QueryAsync(queryable => Filter(queryable, filter), cancellationToken).ToList();
    }

    private static IQueryable<Role> Filter(IQueryable<Role> query, RoleFilter filter) => filter.Apply(query);
}