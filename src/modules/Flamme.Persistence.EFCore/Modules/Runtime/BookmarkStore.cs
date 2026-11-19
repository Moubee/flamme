using Flamme.Common.Models;
using Flamme.Extensions;
using Flamme.Workflows;
using Flamme.Workflows.Runtime;
using Flamme.Workflows.Runtime.Entities;
using Flamme.Workflows.Runtime.Filters;
using JetBrains.Annotations;

namespace Flamme.Persistence.EFCore.Modules.Runtime;

/// <summary>
/// An EF Core implementation of <see cref="IBookmarkStore"/>.
/// </summary>
[UsedImplicitly]
public class EFCoreBookmarkStore(Store<RuntimeFlammeDbContext, StoredBookmark> store, IPayloadSerializer serializer) : IBookmarkStore
{
    /// <inheritdoc />
    public async ValueTask SaveAsync(StoredBookmark record, CancellationToken cancellationToken = default)
    {
        await store.SaveAsync(record, s => s.Id, OnSaveAsync, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask SaveManyAsync(IEnumerable<StoredBookmark> records, CancellationToken cancellationToken)
    {
        await store.SaveManyAsync(records, s => s.Id, OnSaveAsync, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<StoredBookmark?> FindAsync(BookmarkFilter filter, CancellationToken cancellationToken = default)
    {
        return await store.FindAsync(filter.Apply, OnLoadAsync, filter.TenantAgnostic, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<IEnumerable<StoredBookmark>> FindManyAsync(BookmarkFilter filter, CancellationToken cancellationToken = default)
    {
        return await store.QueryAsync(filter.Apply, OnLoadAsync, filter.TenantAgnostic, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<Page<StoredBookmark>> FindManyAsync(BookmarkFilter filter, PageArgs pageArgs, CancellationToken cancellationToken = default)
    {
        var count = await store.CountAsync(filter.Apply, filter.TenantAgnostic, cancellationToken);
        var results = (await store.QueryAsync(query => filter.Apply(query).OrderBy(x => x.Id).Paginate(pageArgs), OnLoadAsync, filter.TenantAgnostic, cancellationToken)).ToList();
        return Page.Of(results, count);
    }

    /// <inheritdoc />
    public async ValueTask<long> DeleteAsync(BookmarkFilter filter, CancellationToken cancellationToken = default)
    {
        return await store.DeleteWhereAsync(filter.Apply, cancellationToken);
    }

    private ValueTask OnSaveAsync(RuntimeFlammeDbContext dbContext, StoredBookmark entity, CancellationToken cancellationToken)
    {
        dbContext.Entry(entity).Property("SerializedPayload").CurrentValue = entity.Payload != null ? serializer.Serialize(entity.Payload) : null;
        dbContext.Entry(entity).Property("SerializedMetadata").CurrentValue = entity.Metadata != null ? serializer.Serialize(entity.Metadata) : null;
        return default;
    }

    private ValueTask OnLoadAsync(RuntimeFlammeDbContext dbContext, StoredBookmark? entity, CancellationToken cancellationToken)
    {
        if (entity is null)
            return default;

        var payloadJson = dbContext.Entry(entity).Property<string>("SerializedPayload").CurrentValue;
        var metadataJson = dbContext.Entry(entity).Property<string>("SerializedMetadata").CurrentValue;
        entity.Payload = !string.IsNullOrEmpty(payloadJson) ? serializer.Deserialize(payloadJson) : null;
        entity.Metadata = !string.IsNullOrEmpty(metadataJson) ? serializer.Deserialize<Dictionary<string, string>>(metadataJson) : null;

        return default;
    }
}
