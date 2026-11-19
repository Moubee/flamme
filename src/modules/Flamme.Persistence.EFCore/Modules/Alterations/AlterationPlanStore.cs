using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Flamme.Alterations.Core.Contracts;
using Flamme.Alterations.Core.Entities;
using Flamme.Alterations.Core.Filters;
using Flamme.Alterations.Core.Models;
using Flamme.Alterations.Core.Stores;
using Flamme.Tenants.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Flamme.Persistence.EFCore.Modules.Alterations;

/// <summary>
/// An EF Core implementation of <see cref="IAlterationPlanStore"/>.
/// </summary>
public class EFCoreAlterationPlanStore : IAlterationPlanStore
{
    private readonly EntityStore<AlterationsFlammeDbContext, AlterationPlan> _store;
    private readonly IAlterationSerializer _alterationSerializer;
    private readonly bool _tenantEnabled;

    /// <summary>
    /// Constructor.
    /// </summary>
    public EFCoreAlterationPlanStore(
        EntityStore<AlterationsFlammeDbContext, AlterationPlan> store,
        IAlterationSerializer alterationSerializer)
        : this(store, alterationSerializer, Options.Create(new TenantsOptions()))
    {
    }

    /// <summary>
    /// Constructor used by dependency injection. Direct construction through the legacy
    /// overload keeps tenancy-aware upsert disabled for compatibility.
    /// </summary>
    [ActivatorUtilitiesConstructor]
    public EFCoreAlterationPlanStore(
        EntityStore<AlterationsFlammeDbContext, AlterationPlan> store,
        IAlterationSerializer alterationSerializer,
        IOptions<TenantsOptions> tenantsOptions)
    {
        _store = store;
        _alterationSerializer = alterationSerializer;
        _tenantEnabled = tenantsOptions.Value.IsEnabled;
    }

    /// <inheritdoc />
    public async Task SaveAsync(AlterationPlan record, CancellationToken cancellationToken = default)
    {
        if (!_tenantEnabled)
        {
            await _store.SaveAsync(record, OnSaveAsync, cancellationToken);
            return;
        }

        await using var dbContext = await _store.CreateDbContextAsync(cancellationToken);
        var ambientTenantId = AlterationTenantOwnedUpsert.AmbientTenantId(dbContext);
        AlterationTenantOwnedUpsert.StampTenantId(record, ambientTenantId);
        OnSave(dbContext, record);

        var status = record.Status;
        var createdAt = record.CreatedAt;
        var startedAt = record.StartedAt;
        var completedAt = record.CompletedAt;
        var serializedAlterations = dbContext.Entry(record).Property<string>("SerializedAlterations").CurrentValue;
        var serializedFilter = dbContext.Entry(record).Property<string>("SerializedWorkflowInstanceFilter").CurrentValue;

        var query = dbContext.Set<AlterationPlan>()
            .IgnoreQueryFilters()
            .Where(AlterationTenantOwnedUpsert.OwnedId<AlterationPlan>(record.Id, record.TenantId, ambientTenantId));

        // Inline lambda so net8/net9 bind SetPropertyCalls and net10 binds UpdateSettersBuilder.
        Task<int> UpdateOwnedAsync() => query.ExecuteUpdateAsync(
            setters => setters
                .SetProperty(plan => plan.Status, status)
                .SetProperty(plan => plan.CreatedAt, createdAt)
                .SetProperty(plan => plan.StartedAt, startedAt)
                .SetProperty(plan => plan.CompletedAt, completedAt)
                .SetProperty(plan => EF.Property<string>(plan, "SerializedAlterations"), serializedAlterations)
                .SetProperty(plan => EF.Property<string>(plan, "SerializedWorkflowInstanceFilter"), serializedFilter),
            cancellationToken);

        var updated = await _store.ExecuteWithDbExceptionHandlingAsync(UpdateOwnedAsync, cancellationToken);

        if (updated == 0)
        {
            var inserted = await _store.ExecuteWithDbExceptionHandlingAsync(
                () => AlterationTenantOwnedUpsert.InsertIfAbsentAsync(dbContext, record, cancellationToken),
                cancellationToken);
            if (!inserted)
            {
                var retried = await _store.ExecuteWithDbExceptionHandlingAsync(UpdateOwnedAsync, cancellationToken);

                if (retried == 0)
                    throw AlterationStoreConflict.HiddenPlanId(record.Id);
            }
        }
    }

    /// <inheritdoc />
    [RequiresUnreferencedCode("Calls System.Text.Json.JsonSerializer.Deserialize<TValue>(String, JsonSerializerOptions)")]
    public async Task<AlterationPlan?> FindAsync(AlterationPlanFilter filter, CancellationToken cancellationToken = default)
    {
        return await _store.FindAsync(filter.Apply, OnLoadAsync, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<long> CountAsync(AlterationPlanFilter filter, CancellationToken cancellationToken = default)
    {
        return await _store.CountAsync(filter.Apply, cancellationToken);
    }

    private void OnSave(AlterationsFlammeDbContext flammeDbContext, AlterationPlan entity)
    {
        flammeDbContext.Entry(entity).Property("SerializedAlterations").CurrentValue = _alterationSerializer.SerializeMany(entity.Alterations);
        flammeDbContext.Entry(entity).Property("SerializedWorkflowInstanceFilter").CurrentValue = JsonSerializer.Serialize(entity.WorkflowInstanceFilter);
    }

    private ValueTask OnSaveAsync(AlterationsFlammeDbContext dbContext, AlterationPlan entity, CancellationToken cancellationToken)
    {
        OnSave(dbContext, entity);
        return default;
    }

    [RequiresUnreferencedCode("Calls System.Text.Json.JsonSerializer.Deserialize<TValue>(String, JsonSerializerOptions)")]
    private ValueTask OnLoadAsync(AlterationsFlammeDbContext flammeDbContext, AlterationPlan? entity, CancellationToken cancellationToken)
    {
        if (entity is null)
            return default;

        var alterationsJson = flammeDbContext.Entry(entity).Property<string>("SerializedAlterations").CurrentValue;
        var workflowInstanceFilterJson = flammeDbContext.Entry(entity).Property<string>("SerializedWorkflowInstanceFilter").CurrentValue;
        entity.Alterations = _alterationSerializer.DeserializeMany(alterationsJson).ToList();
        entity.WorkflowInstanceFilter = JsonSerializer.Deserialize<AlterationWorkflowInstanceFilter>(workflowInstanceFilterJson)!;

        return default;
    }
}
