using Flamme.Features.Attributes;
using Flamme.Features.Services;
using Flamme.KeyValues.Entities;
using Flamme.KeyValues.Features;
using Flamme.Workflows.Runtime;
using Flamme.Workflows.Runtime.Entities;
using Flamme.Workflows.Runtime.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Flamme.Persistence.EFCore.Modules.Runtime;

/// <summary>
/// Configures the default workflow runtime to use EF Core persistence providers.
/// </summary>
[DependsOn(typeof(WorkflowRuntimeFeature))]
public class EFCoreWorkflowRuntimePersistenceFeature(IModule module) : PersistenceFeatureBase<EFCoreWorkflowRuntimePersistenceFeature, RuntimeFlammeDbContext>(module)
{
    /// <summary>
    /// Gets or sets whether to wrap workflow commit persistence in an EF Core transaction.
    /// </summary>
    public bool UseWorkflowCommitTransaction { get; set; }

    /// <inheritdoc />
    public override void Configure()
    {
        Module.Configure<KeyValueFeature>(feature =>
        {
            feature.KeyValueStore = sp => sp.GetRequiredService<EFCoreKeyValueStore>();
        });
        Module.Configure<WorkflowRuntimeFeature>(feature =>
        {
            feature.TriggerStore = sp => sp.GetRequiredService<EFCoreTriggerStore>();
            feature.BookmarkStore = sp => sp.GetRequiredService<EFCoreBookmarkStore>();
            feature.BookmarkQueueStore = sp => sp.GetRequiredService<EFBookmarkQueueStore>();
            feature.BookmarkQueueDeadLetterStore = sp => sp.GetRequiredService<EFBookmarkQueueDeadLetterStore>();
            feature.WorkflowExecutionLogStore = sp => sp.GetRequiredService<EFCoreWorkflowExecutionLogStore>();
            feature.ActivityExecutionLogStore = sp => sp.GetRequiredService<EFCoreActivityExecutionStore>();
        });
    }

    /// <inheritdoc />
    public override void Apply()
    {
        base.Apply();
        AddEntityStore<StoredTrigger, EFCoreTriggerStore>();
        AddStore<StoredBookmark, EFCoreBookmarkStore>();
        AddStore<BookmarkQueueItem, EFBookmarkQueueStore>();
        AddStore<BookmarkQueueDeadLetterItem, EFBookmarkQueueDeadLetterStore>();
        AddEntityStore<WorkflowExecutionLogRecord, EFCoreWorkflowExecutionLogStore>();
        AddEntityStore<ActivityExecutionRecord, EFCoreActivityExecutionStore>();
        AddStore<SerializedKeyValuePair, EFCoreKeyValueStore>();

        if (UseWorkflowCommitTransaction)
            Services.AddScoped<IWorkflowCommitTransaction, EFCoreWorkflowCommitTransaction>();
    }
}
