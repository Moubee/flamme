using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Text.Json.Serialization;
using Flamme.Common.Entities;
using Flamme.Common.Models;
using Flamme.Extensions;
using Flamme.Workflows;
using Flamme.Workflows.Management;
using Flamme.Workflows.Management.Entities;
using Flamme.Workflows.Management.Filters;
using Flamme.Workflows.Management.Exceptions;
using Flamme.Workflows.Management.Models;
using Flamme.Workflows.Memory;
using Flamme.Workflows.Models;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Open.Linq.AsyncExtensions;

namespace Flamme.Persistence.EFCore.Modules.Management;

/// <inheritdoc />
[UsedImplicitly]
public partial class EFCoreWorkflowDefinitionStore(EntityStore<ManagementFlammeDbContext, WorkflowDefinition> store, IPayloadSerializer payloadSerializer, ILogger<EFCoreWorkflowDefinitionStore> logger)
    : IWorkflowDefinitionStore
{
    /// <inheritdoc />
    public async Task<WorkflowDefinition?> FindAsync(WorkflowDefinitionFilter filter, CancellationToken cancellationToken = default)
    {
        var orderBy = new WorkflowDefinitionOrder<DateTimeOffset>(x => x.CreatedAt, OrderDirection.Ascending);
        return await FindAsync(filter, orderBy, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<WorkflowDefinition?> FindAsync<TOrderBy>(WorkflowDefinitionFilter filter, WorkflowDefinitionOrder<TOrderBy> order, CancellationToken cancellationToken = default)
    {
        return await store.QueryAsync(queryable => Filter(queryable, filter).OrderBy(order), OnLoadAsync, filter.TenantAgnostic, cancellationToken).FirstOrDefault();
    }

    /// <inheritdoc />
    public async Task<Page<WorkflowDefinition>> FindManyAsync(WorkflowDefinitionFilter filter, PageArgs pageArgs, CancellationToken cancellationToken = default)
    {
        var orderBy = new WorkflowDefinitionOrder<DateTimeOffset>(x => x.CreatedAt, OrderDirection.Ascending);
        return await FindManyAsync(filter, orderBy, pageArgs, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Page<WorkflowDefinition>> FindManyAsync<TOrderBy>(WorkflowDefinitionFilter filter, WorkflowDefinitionOrder<TOrderBy> order, PageArgs pageArgs, CancellationToken cancellationToken = default)
    {
        var count = await store.QueryAsync(queryable => Filter(queryable, filter), cancellationToken).LongCount();
        var results = await store.QueryAsync(queryable => Filter(queryable, filter).OrderBy(order).Paginate(pageArgs), OnLoadAsync, filter.TenantAgnostic, cancellationToken).ToList();
        return new(results, count);
    }

    /// <inheritdoc />
    public async Task<IEnumerable<WorkflowDefinition>> FindManyAsync(WorkflowDefinitionFilter filter, CancellationToken cancellationToken = default)
    {
        var orderBy = new WorkflowDefinitionOrder<DateTimeOffset>(x => x.CreatedAt, OrderDirection.Ascending);
        return await FindManyAsync(filter, orderBy, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IEnumerable<WorkflowDefinition>> FindManyAsync<TOrderBy>(WorkflowDefinitionFilter filter, WorkflowDefinitionOrder<TOrderBy> order, CancellationToken cancellationToken = default)
    {
        return await store.QueryAsync(queryable => Filter(queryable, filter).OrderBy(order), OnLoadAsync, filter.TenantAgnostic, cancellationToken).ToList();
    }

    /// <inheritdoc />
    [RequiresUnreferencedCode("The method 'FindSummariesAsync' is used for serialization and requires unreferenced code to be preserved.")]
    public async Task<Page<WorkflowDefinitionSummary>> FindSummariesAsync(WorkflowDefinitionFilter filter, PageArgs pageArgs, CancellationToken cancellationToken = default)
    {
        var orderBy = new WorkflowDefinitionOrder<DateTimeOffset>(x => x.CreatedAt, OrderDirection.Ascending);
        return await FindSummariesAsync(filter, orderBy, pageArgs, cancellationToken);
    }

    /// <inheritdoc />
    [RequiresUnreferencedCode("The method 'FindSummariesAsync' is used for serialization and requires unreferenced code to be preserved.")]
    public async Task<Page<WorkflowDefinitionSummary>> FindSummariesAsync<TOrderBy>(WorkflowDefinitionFilter filter, WorkflowDefinitionOrder<TOrderBy> order, PageArgs pageArgs, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await store.CreateDbContextAsync(cancellationToken);
        var set = dbContext.WorkflowDefinitions.AsNoTracking();
        var queryable = Filter(set.AsQueryable(), filter).OrderBy(order);
        
        if (filter.TenantAgnostic)
            queryable = queryable.IgnoreQueryFilters();

        var count = await queryable.LongCountAsync(cancellationToken);
        queryable = Paginate(queryable, pageArgs);
        var results = await queryable.Select(WorkflowDefinitionSummary.FromDefinitionExpression()).ToListAsync(cancellationToken);
        return Page.Of(results, count);
    }

    /// <inheritdoc />
    [RequiresUnreferencedCode("The method 'FindSummariesAsync' is used for serialization and requires unreferenced code to be preserved.")]
    public async Task<IEnumerable<WorkflowDefinitionSummary>> FindSummariesAsync(WorkflowDefinitionFilter filter, CancellationToken cancellationToken = default)
    {
        var orderBy = new WorkflowDefinitionOrder<DateTimeOffset>(x => x.CreatedAt, OrderDirection.Ascending);
        return await FindSummariesAsync(filter, orderBy, cancellationToken);
    }

    /// <inheritdoc />
    [RequiresUnreferencedCode("The method 'FindSummariesAsync' is used for serialization and requires unreferenced code to be preserved.")]
    public async Task<IEnumerable<WorkflowDefinitionSummary>> FindSummariesAsync<TOrderBy>(WorkflowDefinitionFilter filter, WorkflowDefinitionOrder<TOrderBy> order, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await store.CreateDbContextAsync(cancellationToken);
        var set = dbContext.WorkflowDefinitions.AsNoTracking();
        var queryable = Filter(set.AsQueryable(), filter).OrderBy(order);
        
        if (filter.TenantAgnostic)
            queryable = queryable.IgnoreQueryFilters();
        
        return await queryable.Select(WorkflowDefinitionSummary.FromDefinitionExpression()).ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<WorkflowDefinition?> FindLastVersionAsync(WorkflowDefinitionFilter filter, CancellationToken cancellationToken)
    {
        return await store.QueryAsync(queryable => Filter(queryable, filter).OrderByDescending(x => x.Version), OnLoadAsync, filter.TenantAgnostic, cancellationToken).FirstOrDefault();
    }

    /// <inheritdoc />
    public async Task<bool> TryAddAsync(WorkflowDefinition definition, CancellationToken cancellationToken = default)
    {
        try
        {
            await WriteDefinitionsAsync([definition], DefinitionWrite.Insert, cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) when (DbExceptionClassifier.IsDuplicateKey(ex))
        {
            return false;
        }
    }

    /// <inheritdoc />
    public Task SaveAsync(WorkflowDefinition definition, CancellationToken cancellationToken = default) =>
        WriteDefinitionsAsync([definition], DefinitionWrite.Save, cancellationToken);

    /// <inheritdoc />
    public Task SavePublicationAsync(IEnumerable<WorkflowDefinition> definitions, CancellationToken cancellationToken = default) =>
        WriteDefinitionsAsync(definitions.ToList(), DefinitionWrite.Publish, cancellationToken);

    /// <inheritdoc />
    public Task SaveDraftVersionAsync(WorkflowDefinition? previous, WorkflowDefinition draft, CancellationToken cancellationToken = default) =>
        WriteDefinitionsAsync(previous is null ? [draft] : [previous, draft], DefinitionWrite.Draft, cancellationToken);

    /// <inheritdoc />
    public Task SaveManyAsync(IEnumerable<WorkflowDefinition> definitions, CancellationToken cancellationToken = default) =>
        WriteDefinitionsAsync(definitions.ToList(), DefinitionWrite.Bulk, cancellationToken);

    private enum DefinitionWrite { Insert, Save, Publish, Draft, Bulk }

    private async Task WriteDefinitionsAsync(List<WorkflowDefinition> definitions, DefinitionWrite kind, CancellationToken cancellationToken)
    {
        if (definitions.Count == 0)
            return;

        await store.ExecuteWithDbExceptionHandlingAsync(async () =>
        {
            await using var db = await store.CreateDbContextAsync(cancellationToken);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
            await LockDefinitionsAsync(db, definitions, cancellationToken);
            if (kind == DefinitionWrite.Bulk)
                await store.BulkSaveInContextAsync(db, definitions, x => x.Id, OnSaveAsync, cancellationToken);
            else
            {
                var revision = definitions[^1];
                if (kind is DefinitionWrite.Publish or DefinitionWrite.Draft)
                {
                    // Reload and demote the actual rows, so a stale publisher cannot overwrite a newer head or its metadata.
                    var predecessors = await db.WorkflowDefinitions
                        .Where(x => x.DefinitionId == revision.DefinitionId && (kind == DefinitionWrite.Draft || x.Id != revision.Id) &&
                            (x.IsLatest || (kind == DefinitionWrite.Publish && x.IsPublished)))
                        .ToListAsync(cancellationToken);
                    if (!predecessors.Select(x => x.Id).ToHashSet().SetEquals(definitions.SkipLast(1).Select(x => x.Id)) ||
                        (kind == DefinitionWrite.Draft && predecessors.Count == 1 && predecessors[0].IsPublished != definitions[0].IsPublished))
                        throw new WorkflowDefinitionValidationException("The workflow head changed before saving. Reload the definition and try again.");
                    foreach (var predecessor in predecessors)
                    {
                        if (predecessor.Id == revision.Id)
                        {
                            if (predecessor.IsPublished && !revision.IsPublished)
                                throw new WorkflowDefinitionValidationException("This draft has already been published. Reload it before saving a new draft.");
                            db.Entry(predecessor).State = EntityState.Detached;
                            continue;
                        }
                        await OnLoadAsync(db, predecessor, cancellationToken);
                        predecessor.IsLatest = false;
                        if (kind == DefinitionWrite.Publish)
                            predecessor.IsPublished = false;
                    }
                }

                await OnSaveAsync(db, revision, cancellationToken);
                var exists = kind != DefinitionWrite.Insert && await db.WorkflowDefinitions.AnyAsync(x => x.Id == revision.Id, cancellationToken);
                db.Entry(revision).State = exists ? EntityState.Modified : EntityState.Added;
                await db.SaveChangesAsync(cancellationToken);
            }

            await ValidateActivityBindingsAsync(db, definitions, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }, cancellationToken, ex => ex is not WorkflowDefinitionValidationException);
    }

    private static async Task LockDefinitionsAsync(ManagementFlammeDbContext db, IEnumerable<WorkflowDefinition> definitions, CancellationToken cancellationToken)
    {
        // The PostgreSQL transaction owns these locks even when the definition has no rows yet.
        foreach (var id in definitions.Select(x => x.DefinitionId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var resource = $"workflow-definition:{db.Schema}:{id}";
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({resource}, 0))", cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task<WorkflowDefinitionUpdateResult> TryUpdateLatestAsync(
        WorkflowDefinitionFilter filter,
        Func<WorkflowDefinition, bool> matchesExpected,
        Func<WorkflowDefinition, WorkflowDefinition> update,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await store.CreateDbContextAsync(cancellationToken);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        var queryable = Filter(dbContext.WorkflowDefinitions, filter);

        if (filter.TenantAgnostic)
            queryable = queryable.IgnoreQueryFilters();

        var current = await queryable.OrderBy(x => x.CreatedAt).FirstOrDefaultAsync(cancellationToken);

        if (current == null)
            return WorkflowDefinitionUpdateResult.NotFound();

        await OnLoadAsync(dbContext, current, cancellationToken);

        if (!matchesExpected(current))
            return WorkflowDefinitionUpdateResult.Conflict();

        var expectedId = current.Id;
        var expectedVersion = current.Version;
        var expectedStringData = current.StringData;
        var expectedName = current.Name;
        var expectedDescription = current.Description;
        var expectedData = (string?)dbContext.Entry(current).Property("Data").CurrentValue;

        // Materialize the shadow Data column before detaching the compare-and-swap snapshot.
        dbContext.Entry(current).State = EntityState.Detached;
        var next = update(current);
        await LockDefinitionsAsync(dbContext, [next], cancellationToken);
        var nextData = SerializeState(next);
        var nextUsableAsActivity = next.Options.UsableAsActivity;

        if (next.Id != expectedId)
        {
            var unmarked = await dbContext.WorkflowDefinitions
                .Where(MatchesLoadedSnapshot(expectedId, expectedVersion, expectedStringData, expectedName, expectedDescription, expectedData))
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.IsLatest, false), cancellationToken);

            if (unmarked == 0)
                return WorkflowDefinitionUpdateResult.Conflict();

            dbContext.WorkflowDefinitions.Add(next);
            dbContext.Entry(next).Property("Data").CurrentValue = nextData;
            dbContext.Entry(next).Property("UsableAsActivity").CurrentValue = nextUsableAsActivity;
            await dbContext.SaveChangesAsync(cancellationToken);
            await ValidateActivityBindingsAsync(dbContext, [next], cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return WorkflowDefinitionUpdateResult.Updated(next);
        }

        var updated = await dbContext.WorkflowDefinitions
            .Where(MatchesLoadedSnapshot(expectedId, expectedVersion, expectedStringData, expectedName, expectedDescription, expectedData))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.StringData, next.StringData)
                    .SetProperty(x => EF.Property<string?>(x, "Data"), nextData)
                    .SetProperty(x => EF.Property<bool?>(x, "UsableAsActivity"), nextUsableAsActivity),
                cancellationToken);

        if (updated == 0)
            return WorkflowDefinitionUpdateResult.Conflict();

        await ValidateActivityBindingsAsync(dbContext, [next], cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return WorkflowDefinitionUpdateResult.Updated(next);
    }

    /// <summary>
    /// The loaded snapshot <see cref="TryUpdateLatestAsync"/> will only overwrite: identity, the ETag-covered
    /// graph, the serialized <c>Data</c> blob (source XML, variables, options) and the name/description columns
    /// a metadata-only save changes. The row must still be <c>IsLatest</c> so a published→draft loser
    /// is Conflict rather than a unique-key failure on <c>(DefinitionId, Version)</c>. Zero rows means
    /// another writer got there first.
    /// </summary>
    private static Expression<Func<WorkflowDefinition, bool>> MatchesLoadedSnapshot(
        string expectedId,
        int expectedVersion,
        string? expectedStringData,
        string? expectedName,
        string? expectedDescription,
        string? expectedData) =>
        x => x.Id == expectedId
             && x.Version == expectedVersion
             && x.IsLatest
             && x.StringData == expectedStringData
             && x.Name == expectedName
             && x.Description == expectedDescription
             && EF.Property<string?>(x, "Data") == expectedData;

    /// <inheritdoc />
    public async Task<long> DeleteAsync(WorkflowDefinitionFilter filter, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await store.CreateDbContextAsync(cancellationToken);
        var set = dbContext.WorkflowDefinitions;
        var queryable = set.AsQueryable();
        
        if (filter.TenantAgnostic)
            queryable = queryable.IgnoreQueryFilters();
        
        var ids = await Filter(queryable, filter).Select(x => x.Id).Distinct().ToListAsync(cancellationToken);
        return await store.DeleteWhereAsync(x => ids.Contains(x.Id), cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> AnyAsync(WorkflowDefinitionFilter filter, CancellationToken cancellationToken = default)
    {
        return await store.QueryAsync(queryable => Filter(queryable, filter), filter.TenantAgnostic, cancellationToken).Any();
    }

    /// <inheritdoc />
    public async Task<long> CountDistinctAsync(CancellationToken cancellationToken = default)
    {
        return await store.CountAsync(x => true, x => x.DefinitionId, false, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> GetIsNameUnique(string name, string? definitionId = null, CancellationToken cancellationToken = default)
    {
        var exists = await store.AnyAsync(x => x.Name == name && x.DefinitionId != definitionId, false, cancellationToken);
        return !exists;
    }

    private ValueTask OnSaveAsync(ManagementFlammeDbContext managementFlammeDbContext, WorkflowDefinition entity, CancellationToken cancellationToken)
    {
        var json = SerializeState(entity);

        managementFlammeDbContext.Entry(entity).Property("Data").CurrentValue = json;
        managementFlammeDbContext.Entry(entity).Property("UsableAsActivity").CurrentValue = entity.Options.UsableAsActivity;
        return ValueTask.CompletedTask;
    }

    private string SerializeState(WorkflowDefinition entity)
    {
        var data = new WorkflowDefinitionState(entity.Options, entity.Variables, entity.Inputs, entity.Outputs, entity.Outcomes, entity.CustomProperties);
        return payloadSerializer.Serialize(data);
    }

    private ValueTask OnLoadAsync(ManagementFlammeDbContext managementFlammeDbContext, WorkflowDefinition? entity, CancellationToken cancellationToken)
    {
        if (entity == null)
            return ValueTask.CompletedTask;

        var data = new WorkflowDefinitionState(entity.Options, entity.Variables, entity.Inputs, entity.Outputs, entity.Outcomes, entity.CustomProperties);
        var json = (string?)managementFlammeDbContext.Entry(entity).Property("Data").CurrentValue;

        try
        {
            if (!string.IsNullOrWhiteSpace(json))
                data = payloadSerializer.Deserialize<WorkflowDefinitionState>(json);
        }
        catch (Exception exp)
        {
            logger.LogError(exp, "Could not deserialize workflow definition state: {DefinitionId}. Reverting to default state", entity.DefinitionId);
        }

        entity.Options = data.Options;
        entity.Variables = data.Variables;
        entity.Inputs = data.Inputs;
        entity.Outputs = data.Outputs;
        entity.Outcomes = data.Outcomes;
        entity.CustomProperties = data.CustomProperties;

        return ValueTask.CompletedTask;
    }

    private IQueryable<WorkflowDefinition> Filter(IQueryable<WorkflowDefinition> queryable, WorkflowDefinitionFilter filter)
    {
        var definitionId = filter.DefinitionId ?? filter.DefinitionHandle?.DefinitionId;
        var versionOptions = filter.VersionOptions ?? filter.DefinitionHandle?.VersionOptions;
        var id = filter.Id ?? filter.DefinitionHandle?.DefinitionVersionId;
        
        if (definitionId != null) queryable = queryable.Where(x => x.DefinitionId == definitionId);
        if (filter.DefinitionIds != null) queryable = queryable.Where(x => filter.DefinitionIds.Contains(x.DefinitionId));
        if (id != null) queryable = queryable.Where(x => x.Id == id);
        if (filter.Ids != null) queryable = queryable.Where(x => filter.Ids.Contains(x.Id));
        if (versionOptions != null) queryable = queryable.WithVersion(versionOptions.Value);
        if (filter.MaterializerName != null) queryable = queryable.Where(x => x.MaterializerName == filter.MaterializerName);
        if (filter.Name != null) queryable = queryable.Where(x => x.Name == filter.Name);
        if (filter.Names != null) queryable = queryable.Where(x => filter.Names.Contains(x.Name!));
        if (filter.UsableAsActivity != null) queryable = queryable.Where(x => EF.Property<bool>(x, "UsableAsActivity") == filter.UsableAsActivity);
        if (!string.IsNullOrWhiteSpace(filter.SearchTerm)) queryable = queryable.Where(x => x.Name!.ToLower().Contains(filter.SearchTerm.ToLower()) || x.Description!.ToLower().Contains(filter.SearchTerm.ToLower()) || x.Id.Contains(filter.SearchTerm) || x.DefinitionId.Contains(filter.SearchTerm));

        // TEMP: IsSystem may be null when upgrading from older versions of Flamme to 3.2. See issue #5366.
        // In a future version, we should remove this check and simply do queryable.Where(x => x.IsSystem == filter.IsSystem).
        if (filter.IsSystem != null)
            queryable = filter.IsSystem == true
                ? queryable.Where(x => x.IsSystem == true)
#pragma warning disable CS0472 // The result of the expression is always the same since a value of this type is never equal to 'null'
                : queryable.Where(x => x.IsSystem == false || x.IsSystem == null!);
#pragma warning restore CS0472 // The result of the expression is always the same since a value of this type is never equal to 'null'

        if (filter.IsReadonly != null) queryable = queryable.Where(x => x.IsReadonly == filter.IsReadonly);
        return queryable;
    }

    private IQueryable<WorkflowDefinition> Paginate(IQueryable<WorkflowDefinition> queryable, PageArgs? pageArgs)
    {
        if (pageArgs?.Offset != null) queryable = queryable.Skip(pageArgs.Offset.Value);
        if (pageArgs?.Limit != null) queryable = queryable.Take(pageArgs.Limit.Value);
        return queryable;
    }

    private class WorkflowDefinitionState
    {
        [JsonConstructor]
        public WorkflowDefinitionState()
        {
        }

        public WorkflowDefinitionState(
            WorkflowOptions options,
            ICollection<Variable> variables,
            ICollection<InputDefinition> inputs,
            ICollection<OutputDefinition> outputs,
            ICollection<string> outcomes,
            IDictionary<string, object> customProperties
        )
        {
            Options = options;
            Variables = variables;
            Inputs = inputs;
            Outputs = outputs;
            Outcomes = outcomes;
            CustomProperties = customProperties;
        }

        public WorkflowOptions Options { get; set; } = new();
        public ICollection<Variable> Variables { get; set; } = new List<Variable>();
        public ICollection<InputDefinition> Inputs { get; set; } = new List<InputDefinition>();
        public ICollection<OutputDefinition> Outputs { get; set; } = new List<OutputDefinition>();
        public ICollection<string> Outcomes { get; set; } = new List<string>();
        public IDictionary<string, object> CustomProperties { get; set; } = new Dictionary<string, object>();
    }
}
