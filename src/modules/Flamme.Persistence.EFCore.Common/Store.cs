using System.Linq.Expressions;
using Flamme.Common.Entities;
using Flamme.Common.Models;
using Flamme.Common.Multitenancy;
using Flamme.Persistence.EFCore.Extensions;
using Flamme.Extensions;
using Flamme.Tenants.Options;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Open.Linq.AsyncExtensions;

namespace Flamme.Persistence.EFCore;

/// <summary>
/// A generic repository class around EF Core for accessing entities.
/// </summary>
/// <typeparam name="TDbContext">The type of the database context.</typeparam>
/// <typeparam name="TEntity">The type of the entity.</typeparam>
[PublicAPI]
public class Store<TDbContext, TEntity>(IDbContextFactory<TDbContext> dbContextFactory, IServiceProvider serviceProvider) where TDbContext : DbContext where TEntity : class, new()
{
    /// <summary>
    /// Creates a new instance of the database context.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The database context.</returns>
    public async Task<TDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => await dbContextFactory.CreateDbContextAsync(cancellationToken);

    /// <summary>
    /// Adds the specified entity.
    /// </summary>
    /// <param name="entity">The entity to add.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task AddAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        await AddAsync(entity, null, cancellationToken);
    }

    /// <summary>
    /// Adds the specified entity.
    /// </summary>
    /// <param name="entity">The entity to add.</param>
    /// <param name="onAdding">The callback to invoke before adding the entity.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task AddAsync(TEntity entity, Func<TDbContext, TEntity, CancellationToken, ValueTask>? onAdding, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await CreateDbContextAsync(cancellationToken);

        if (onAdding != null)
            await onAdding(dbContext, entity, cancellationToken);

        var set = dbContext.Set<TEntity>();
        await set.AddAsync(entity, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Adds the specified entities.
    /// </summary>
    /// <param name="entities">The entities to save.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task AddManyAsync(
        IEnumerable<TEntity> entities,
        CancellationToken cancellationToken = default)
    {
        await AddManyAsync(entities, null, cancellationToken);
    }

    /// <summary>
    /// Adds the specified entities.
    /// </summary>
    /// <param name="entities">The entities to save.</param>
    /// <param name="onSaving">The callback to invoke before saving the entity.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task AddManyAsync(
        IEnumerable<TEntity> entities,
        Func<TDbContext, TEntity, CancellationToken, ValueTask>? onSaving = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var entityList = entities.ToList();
            if (entityList.Count == 0)
                return;

            await ExecuteWriteAsync(async (dbContext, ct) =>
            {
                await InvokeSavingCallbacksAsync(dbContext, entityList, onSaving, ct);
                await dbContext.BulkInsertAsync(entityList, ct);
            }, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await HandleDbExceptionAsync(ex, cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// Saves the entity.
    /// </summary>
    /// <param name="entity">The entity to save.</param>
    /// <param name="keySelector">The key selector to get the primary key property.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task SaveAsync(TEntity entity, Expression<Func<TEntity, string>> keySelector, CancellationToken cancellationToken = default) => await SaveAsync(entity, keySelector, null, cancellationToken);

    /// <summary>
    /// Saves the entity. Concurrent PostgreSQL calls to this method for the same mapped key
    /// participate in a transaction-scoped lock. Existing rows are written without checking
    /// an expected version.
    /// </summary>
    /// <remarks>
    /// A native insert can win after the existence read. The resulting unique conflict follows
    /// the configured database exception handling path, with no internal replay of saving hooks.
    /// </remarks>
    /// <param name="entity">The entity to save.</param>
    /// <param name="keySelector">The key selector to get the primary key property.</param>
    /// <param name="onSaving">The callback to invoke before saving the entity.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task SaveAsync(TEntity entity, Expression<Func<TEntity, string>> keySelector, Func<TDbContext, TEntity, CancellationToken, ValueTask>? onSaving, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await using var dbContext = await CreateDbContextAsync(cancellationToken);
            if (onSaving != null)
                await onSaving(dbContext, entity, cancellationToken);

            var entityType = dbContext.Model.FindEntityType(typeof(TEntity));
            var table = entityType == null ? null : StoreObjectIdentifier.Create(entityType, StoreObjectType.Table);
            var keyProperty = entityType?.FindProperty(keySelector.GetProperty()!.Name);

            if (PostgreSqlWriteKeys.Supports(dbContext) && table.HasValue &&
                keyProperty?.GetColumnName(table.Value) != null)
            {
                var key = dbContext.Entry(entity).Property<string>(keyProperty.Name).CurrentValue;
                await PostgreSqlWriteKeys.ExecuteAsync(dbContext,
                    async () =>
                    {
                        await PostgreSqlWriteKeys.AcquireAsync(dbContext, keyProperty, table.Value, key, cancellationToken);
                        return await FindSaveStateAsync(dbContext, entity, keySelector, cancellationToken, (keyProperty, table.Value, key));
                    },
                    preparedState => WriteTrackedAsync(dbContext, entity, preparedState, cancellationToken),
                    cancellationToken);
                return;
            }

            var state = await FindSaveStateAsync(dbContext, entity, keySelector, cancellationToken);
            await WriteTrackedAsync(dbContext, entity, state, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await HandleDbExceptionAsync(ex, cancellationToken);
            throw;
        }
    }

    private async Task<EntityState> FindSaveStateAsync(
        TDbContext dbContext,
        TEntity entity,
        Expression<Func<TEntity, string>> keySelector,
        CancellationToken cancellationToken,
        (IProperty Property, StoreObjectIdentifier Table, string? Value)? mappedKey = null)
    {
        var set = dbContext.Set<TEntity>();
        var query = set.Where(keySelector.BuildEqualsExpression(entity));
        bool exists;

        if (entity is Entity owned && serviceProvider.GetService<IOptions<TenantsOptions>>()?.Value.IsEnabled == true)
        {
            var keyName = mappedKey?.Property.Name ?? keySelector.GetProperty()!.Name;
            var current = await query
                .Select(row => new
                {
                    Key = EF.Property<string>(row, keyName),
                    TenantId = EF.Property<string?>(row, nameof(Entity.TenantId))
                })
                .FirstOrDefaultAsync(cancellationToken);
            exists = current != null;
            if (current != null)
            {
                var writer = serviceProvider.GetRequiredService<ITenantAccessor>().TenantId.NormalizeTenantId();
                var incoming = (owned.TenantId ?? writer).NormalizeTenantId();
                if ((incoming != writer && incoming != Tenant.AgnosticTenantId) ||
                    incoming != current.TenantId.NormalizeTenantId() ||
                    (mappedKey is { } key && !await MatchesTrackedKeyAsync(dbContext, key.Property, key.Table, current.Key, key.Value, cancellationToken)))
                    throw new InvalidOperationException($"Cannot replace {typeof(TEntity).Name}: tenant ownership mismatch.");
            }
        }
        else
            exists = await query.AnyAsync(cancellationToken);

        return exists ? EntityState.Modified : EntityState.Added;
    }

    private static async Task<bool> MatchesTrackedKeyAsync(TDbContext dbContext, IProperty keyProperty,
        StoreObjectIdentifier table, string storedKey, string? submittedKey, CancellationToken cancellationToken)
    {
        var stored = PostgreSqlWriteKeys.GetProviderKey(keyProperty, storedKey);
        var submitted = PostgreSqlWriteKeys.GetProviderKey(keyProperty, submittedKey);
        if (string.Equals(stored, submitted, StringComparison.Ordinal))
            return true;

        if (!string.Equals(stored.TrimEnd(' '), submitted.TrimEnd(' '), StringComparison.Ordinal))
            return false;

        // PostgreSQL character columns can materialize trailing space padding. Check the
        // physical type only for padding candidates, including when EF maps the key as text.
        var tableName = dbContext.GetService<ISqlGenerationHelper>().DelimitIdentifier(table.Name, table.Schema);
        var column = keyProperty.GetColumnName(table)!;
        return await dbContext.Database.SqlQuery<bool>($"""
            SELECT EXISTS (
                SELECT FROM pg_catalog.pg_attribute
                WHERE attrelid = pg_catalog.to_regclass({tableName})
                  AND attname = {column}
                  AND atttypid = 'pg_catalog.bpchar'::pg_catalog.regtype
            ) AS "Value"
            """).SingleAsync(cancellationToken);
    }

    private static Task WriteTrackedAsync(TDbContext dbContext, TEntity entity, EntityState state, CancellationToken cancellationToken)
    {
        dbContext.Set<TEntity>().Entry(entity).State = state;
        return dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Saves the specified entities.
    /// </summary>
    /// <param name="entities">The entities to save.</param>
    /// <param name="keySelector">The key selector to get the primary key property.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task SaveManyAsync(IEnumerable<TEntity> entities, Expression<Func<TEntity, string>> keySelector, CancellationToken cancellationToken = default) => await SaveManyAsync(entities, keySelector, null, cancellationToken);

    /// <summary>
    /// Saves the specified entities.
    /// </summary>
    /// <param name="entities">The entities to save.</param>
    /// <param name="keySelector">The key selector to get the primary key property.</param>
    /// <param name="onSaving">The callback to invoke before saving the entity.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task SaveManyAsync(
        IEnumerable<TEntity> entities,
        Expression<Func<TEntity, string>> keySelector,
        Func<TDbContext, TEntity, CancellationToken, ValueTask>? onSaving = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var entityList = entities.ToList();
            if (entityList.Count == 0)
                return;

            var tenantId = serviceProvider.GetRequiredService<ITenantAccessor>().TenantId.NormalizeTenantId();
            var tenancyEnabled = serviceProvider.GetService<IOptions<TenantsOptions>>()?.Value.IsEnabled == true;

            await ExecuteWriteAsync(async (dbContext, ct) =>
            {
                await InvokeSavingCallbacksAsync(dbContext, entityList, onSaving, ct);

                // Bulk SQL bypasses EF's tenant-stamping hook. Explicit owners, including "*", stay intact.
                foreach (var entity in entityList)
                {
                    if (entity is Entity entityWithTenant)
                        entityWithTenant.TenantId ??= tenantId;
                }

                await dbContext.BulkUpsertWithTenantOwnershipAsync(entityList, keySelector, tenancyEnabled ? tenantId : null, ct);
            }, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await HandleDbExceptionAsync(ex, cancellationToken);
            throw;
        }
    }

    private static async Task InvokeSavingCallbacksAsync(
        TDbContext dbContext,
        IEnumerable<TEntity> entities,
        Func<TDbContext, TEntity, CancellationToken, ValueTask>? onSaving,
        CancellationToken cancellationToken)
    {
        if (onSaving == null)
            return;

        // A batch callback can use EF's change tracker and shadow properties on this context.
        foreach (var entity in entities)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await onSaving(dbContext, entity, cancellationToken);
        }
    }

    private async Task HandleDbExceptionAsync(Exception exception, CancellationToken cancellationToken)
    {
        var handler = serviceProvider.GetService<IDbExceptionHandler>();

        if (handler == null)
            return;

        var context = new DbUpdateExceptionContext(exception, cancellationToken);
        await handler.HandleAsync(context);
    }

    /// <summary>
    /// Executes a database operation and passes failures through the configured database
    /// exception handler.
    /// </summary>
    /// <typeparam name="TResult">The operation result type.</typeparam>
    /// <param name="operation">The database operation to execute.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <param name="shouldHandle">A predicate that excludes exceptions which are not database failures.</param>
    /// <returns>The result returned by <paramref name="operation"/>.</returns>
    internal async Task<TResult> ExecuteWithDbExceptionHandlingAsync<TResult>(
        Func<Task<TResult>> operation,
        CancellationToken cancellationToken = default,
        Func<Exception, bool>? shouldHandle = null)
    {
        try
        {
            return await operation();
        }
        catch (Exception exception) when (shouldHandle is null || shouldHandle(exception))
        {
            await HandleDbExceptionAsync(exception, cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// Executes a database write operation with a fresh context.
    /// </summary>
    internal async Task ExecuteWriteAsync(
        Func<TDbContext, CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await CreateDbContextAsync(cancellationToken);
        await operation(dbContext, cancellationToken);
    }

    /// <summary>
    /// Updates the entity.
    /// </summary>
    /// <param name="entity">The entity to update.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public Task UpdateAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        return UpdateAsync(entity, null, cancellationToken);
    }

    /// <summary>
    /// Updates the entity.
    /// </summary>
    /// <param name="entity">The entity to update.</param>
    /// <param name="onSaving">The callback to invoke before saving the entity.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task UpdateAsync(TEntity entity, Func<TDbContext, TEntity, CancellationToken, ValueTask>? onSaving, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await CreateDbContextAsync(cancellationToken);

        if (onSaving != null)
            await onSaving(dbContext, entity, cancellationToken);

        var set = dbContext.Set<TEntity>();
        set.Entry(entity).State = EntityState.Modified;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Updates specific properties of an entity in the database.
    /// </summary>
    /// <param name="entity">The entity to update.</param>
    /// <param name="properties">An array of expressions indicating the properties to update.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task UpdatePartialAsync(TEntity entity, Expression<Func<TEntity, object>>[] properties, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await CreateDbContextAsync(cancellationToken);
        dbContext.Attach(entity);

        foreach (var property in properties)
            dbContext.Entry(entity).Property(property).IsModified = true;

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Finds the entity matching the specified predicate.
    /// </summary>
    /// <param name="predicate">The predicate to use.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The entity if found, otherwise <c>null</c>.</returns>
    public async Task<TEntity?> FindAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default) => await FindAsync(predicate, null, cancellationToken);

    /// <summary>
    /// Finds the entity matching the specified predicate.
    /// </summary>
    /// <param name="predicate">The predicate to use.</param>
    /// <param name="onLoading">A callback to run after the entity is loaded</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns></returns>
    public async Task<TEntity?> FindAsync(Expression<Func<TEntity, bool>> predicate, Func<TDbContext, TEntity?, TEntity?>? onLoading = null, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await CreateDbContextAsync(cancellationToken);
        var set = dbContext.Set<TEntity>().AsNoTracking();
        var entity = await set.FirstOrDefaultAsync(predicate, cancellationToken);

        if (entity == null)
            return null;

        if (onLoading != null)
            entity = onLoading.Invoke(dbContext, entity);

        return entity;
    }

    /// <summary>
    /// Finds a single entity using a query
    /// </summary>
    /// <param name="query">The query to use</param>
    /// <param name="onLoading">A callback to run after the entity is loaded</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The entity if found, otherwise <c>null</c></returns>
    public async Task<TEntity?> FindAsync(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, Func<TDbContext, TEntity?, CancellationToken, ValueTask>? onLoading = null, CancellationToken cancellationToken = default)
    {
        return await FindAsync(query, onLoading, false, cancellationToken);
    }

    /// <summary>
    /// Finds a single entity using a query
    /// </summary>
    /// <param name="query">The query to use</param>
    /// <param name="onLoading">A callback to run after the entity is loaded</param>
    /// <param name="tenantAgnostic">Define is the request should be tenant agnostic or not</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The entity if found, otherwise <c>null</c></returns>
    public async Task<TEntity?> FindAsync(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, Func<TDbContext, TEntity?, CancellationToken, ValueTask>? onLoading = null, bool tenantAgnostic = false, CancellationToken cancellationToken = default)
    {
        return await QueryAsync(query, onLoading, tenantAgnostic, cancellationToken).FirstOrDefault();
    }

    /// <summary>
    /// Finds a single entity using a query
    /// </summary>
    /// <param name="query">The query to use</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The entity if found, otherwise <c>null</c></returns>
    public async Task<TEntity?> FindAsync(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, CancellationToken cancellationToken = default)
    {
        return await FindAsync(query, false, cancellationToken);
    }

    /// <summary>
    /// Finds a single entity using a query
    /// </summary>
    /// <param name="query">The query to use</param>
    /// <param name="tenantAgnostic">Define is the request should be tenant agnostic or not</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The entity if found, otherwise <c>null</c></returns>
    public async Task<TEntity?> FindAsync(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, bool tenantAgnostic = false, CancellationToken cancellationToken = default)
    {
        return await QueryAsync(query, tenantAgnostic, cancellationToken).FirstOrDefault();
    }

    /// <summary>
    /// Finds a list of entities using a query
    /// </summary>
    public async Task<IEnumerable<TEntity>> FindManyAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default) => await FindManyAsync(predicate, null, cancellationToken);

    /// <summary>
    /// Finds a list of entities using a query
    /// </summary>
    public async Task<IEnumerable<TEntity>> FindManyAsync(Expression<Func<TEntity, bool>> predicate, Action<TDbContext, TEntity?>? onLoading = null, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await CreateDbContextAsync(cancellationToken);
        var set = dbContext.Set<TEntity>().AsNoTracking();
        var entities = await set.Where(predicate).ToListAsync(cancellationToken);

        if (onLoading != null)
            foreach (var entity in entities)
                onLoading(dbContext, entity);

        return entities;
    }

    /// <summary>
    /// Finds a list of entities using a query
    /// </summary>
    public async Task<Page<TEntity>> FindManyAsync<TKey>(
        Expression<Func<TEntity, bool>> predicate,
        Expression<Func<TEntity, TKey>> orderBy,
        OrderDirection orderDirection = OrderDirection.Ascending,
        PageArgs? pageArgs = null,
        CancellationToken cancellationToken = default) =>
        await FindManyAsync(predicate, orderBy, orderDirection, pageArgs, null, cancellationToken);

    /// <summary>
    /// Returns a list of entities using a query
    /// </summary>
    public async Task<Page<TEntity>> FindManyAsync<TKey>(
        Expression<Func<TEntity, bool>>? predicate,
        Expression<Func<TEntity, TKey>>? orderBy,
        OrderDirection orderDirection = OrderDirection.Ascending,
        PageArgs? pageArgs = null,
        Func<TDbContext, TEntity?, TEntity?>? onLoading = null,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await CreateDbContextAsync(cancellationToken);
        var set = dbContext.Set<TEntity>().AsNoTracking();

        if (predicate != null)
            set = set.Where(predicate);

        if (orderBy != null)
            set = orderDirection switch
            {
                OrderDirection.Ascending => set.OrderBy(orderBy),
                OrderDirection.Descending => set.OrderByDescending(orderBy),
                _ => set.OrderBy(orderBy)
            };

        var page = await set.PaginateAsync(pageArgs);

        if (onLoading != null)
            page = page with
            {
                Items = page.Items.Select(x => onLoading(dbContext, x)!).ToList()
            };

        return page;
    }

    public Task<IEnumerable<TEntity>> ListAsync(CancellationToken cancellationToken = default)
    {
        return ListAsync(null, cancellationToken);
    }

    public async Task<IEnumerable<TEntity>> ListAsync(Action<TDbContext, TEntity?>? onLoading = null, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await CreateDbContextAsync(cancellationToken);
        var set = dbContext.Set<TEntity>().AsNoTracking();
        var entities = await set.ToListAsync(cancellationToken);

        if (onLoading != null)
            foreach (var entity in entities)
                onLoading(dbContext, entity);

        return entities;
    }

    /// <summary>
    /// Finds a single entity using a query.
    /// </summary>
    /// <returns>True if the entity was found, otherwise false.</returns>
    public async Task<bool> DeleteAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await CreateDbContextAsync(cancellationToken);
        var set = dbContext.Set<TEntity>();
        set.Attach(entity).State = EntityState.Deleted;
        return await dbContext.SaveChangesAsync(cancellationToken) == 1;
    }

    /// <summary>
    /// Deletes entities using a predicate.
    /// </summary>
    /// <returns>The number of entities deleted.</returns>
    public async Task<long> DeleteWhereAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await CreateDbContextAsync(cancellationToken);
        var set = dbContext.Set<TEntity>().AsNoTracking();
        return await set.Where(predicate).ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>
    /// Deletes entities using a query.
    /// </summary>
    /// <returns>The number of entities deleted.</returns>
    public async Task<long> DeleteWhereAsync(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await CreateDbContextAsync(cancellationToken);
        var set = dbContext.Set<TEntity>().AsNoTracking();
        var queryable = query(set.AsQueryable());
        return await queryable.ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>
    /// Queries the database using a query.
    /// </summary>
    public async Task<IEnumerable<TEntity>> QueryAsync(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, CancellationToken cancellationToken = default)
    {
        return await QueryAsync(query, null, false, cancellationToken);
    }

    /// <summary>
    /// Queries the database using a query.
    /// </summary>
    public async Task<IEnumerable<TEntity>> QueryAsync(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, bool tenantAgnostic, CancellationToken cancellationToken = default)
    {
        return await QueryAsync(query, null, tenantAgnostic, cancellationToken);
    }

    /// <summary>
    /// Queries the database using a query and a selector.
    /// </summary>
    public async Task<IEnumerable<TEntity>> QueryAsync(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, Func<TDbContext, TEntity?, CancellationToken, ValueTask>? onLoading = null, CancellationToken cancellationToken = default)
    {
        return await QueryAsync(query, onLoading, false, cancellationToken);
    }

    /// <summary>
    /// Queries the database using a query and a selector.
    /// </summary>
    public async Task<IEnumerable<TEntity>> QueryAsync(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, Func<TDbContext, TEntity?, CancellationToken, ValueTask>? onLoading = null, bool ignoreQueryFilters = false, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await CreateDbContextAsync(cancellationToken);
        var asNoTracking = onLoading == null;
        var set = asNoTracking ? dbContext.Set<TEntity>().AsNoTracking() : dbContext.Set<TEntity>();
        var queryable = query(set.AsQueryable());

        if (ignoreQueryFilters)
            queryable = queryable.IgnoreQueryFilters();

        var entities = await queryable.ToListAsync(cancellationToken);

        if (onLoading != null)
        {
            var loadingTasks = entities.Select(entity => onLoading(dbContext, entity, cancellationToken).AsTask()).ToList();
            await Task.WhenAll(loadingTasks);
        }

        return entities;
    }

    /// <summary>
    /// Queries the database using a query and a selector.
    /// </summary>
    public async Task<IEnumerable<TResult>> QueryAsync<TResult>(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, Expression<Func<TEntity, TResult>> selector, CancellationToken cancellationToken = default)
    {
        return await QueryAsync(query, selector, false, cancellationToken);
    }

    /// <summary>
    /// Queries the database using a query and a selector.
    /// </summary>
    public async Task<IEnumerable<TResult>> QueryAsync<TResult>(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, Expression<Func<TEntity, TResult>> selector, bool ignoreQueryFilters = false, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await CreateDbContextAsync(cancellationToken);
        var set = dbContext.Set<TEntity>().AsNoTracking();
        var queryable = query(set.AsQueryable());

        if (ignoreQueryFilters)
            queryable = queryable.IgnoreQueryFilters();

        queryable = query(queryable);
        return await queryable.Select(selector).ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Counts the number of entities matching a query.
    /// </summary>
    public async Task<long> CountAsync(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, CancellationToken cancellationToken = default)
    {
        return await CountAsync(query, false, cancellationToken);
    }

    /// <summary>
    /// Counts the number of entities matching a query.
    /// </summary>
    public async Task<long> CountAsync(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, bool ignoreQueryFilters = false, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await CreateDbContextAsync(cancellationToken);
        var set = dbContext.Set<TEntity>().AsNoTracking();
        var queryable = query(set.AsQueryable());

        if (ignoreQueryFilters)
            queryable = queryable.IgnoreQueryFilters();

        queryable = query(queryable);
        return await queryable.LongCountAsync(cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Checks if any entities exist.
    /// </summary>
    public async Task<bool> AnyAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
    {
        return await AnyAsync(predicate, false, cancellationToken);
    }

    /// <summary>
    /// Checks if any entities exist.
    /// </summary>
    public async Task<bool> AnyAsync(Expression<Func<TEntity, bool>> predicate, bool ignoreQueryFilters = false, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await CreateDbContextAsync(cancellationToken);
        var set = dbContext.Set<TEntity>().AsNoTracking();
        return await set.AnyAsync(predicate, cancellationToken);
    }

    /// <summary>
    /// Counts the number of entities matching a predicate.
    /// </summary>
    /// <param name="predicate">The predicate.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task<long> CountAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
    {
        return await CountAsync(predicate, false, cancellationToken);
    }

    /// <summary>
    /// Counts the number of entities matching a predicate.
    /// </summary>
    /// <param name="predicate">The predicate.</param>
    /// <param name="ignoreQueryFilters">Whether to ignore query filters.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task<long> CountAsync(Expression<Func<TEntity, bool>> predicate, bool ignoreQueryFilters = false, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await CreateDbContextAsync(cancellationToken);
        var queryable = dbContext.Set<TEntity>().AsNoTracking();

        if (ignoreQueryFilters)
            queryable = queryable.IgnoreQueryFilters();

        return await queryable.CountAsync(predicate, cancellationToken);
    }

    /// <summary>
    /// Counts the distinct number of entities matching a predicate.
    /// </summary>
    /// <param name="predicate">The predicate.</param>
    /// <param name="propertySelector">The property selector to distinct by.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task<long> CountAsync<TProperty>(Expression<Func<TEntity, bool>> predicate, Expression<Func<TEntity, TProperty>> propertySelector, CancellationToken cancellationToken = default)
    {
        return await CountAsync(predicate, propertySelector, false, cancellationToken);
    }

    /// <summary>
    /// Counts the distinct number of entities matching a predicate.
    /// </summary>
    /// <param name="predicate">The predicate.</param>
    /// <param name="propertySelector">The property selector to distinct by.</param>
    /// <param name="ignoreQueryFilters">Whether to ignore query filters.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task<long> CountAsync<TProperty>(Expression<Func<TEntity, bool>> predicate, Expression<Func<TEntity, TProperty>> propertySelector, bool ignoreQueryFilters = false, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await CreateDbContextAsync(cancellationToken);
        var queryable = dbContext.Set<TEntity>().AsNoTracking();

        if (ignoreQueryFilters)
            queryable = queryable.IgnoreQueryFilters();

        return await queryable
            .Where(predicate)
            .Select(propertySelector)
            .Distinct()
            .CountAsync(cancellationToken);
    }
}
