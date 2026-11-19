using Flamme.KeyValues.Entities;
using Flamme.Workflows.Runtime.Entities;
using Microsoft.EntityFrameworkCore;

namespace Flamme.Persistence.EFCore.Modules.Runtime;

/// <summary>
/// DB context for the runtime module.
/// </summary>
public class RuntimeFlammeDbContext : FlammeDbContextBase
{
    /// <inheritdoc />
    public RuntimeFlammeDbContext(DbContextOptions<RuntimeFlammeDbContext> options, IServiceProvider serviceProvider) : base(options, serviceProvider)
    {
    }

    #region Obsolete

    /// <summary>
    /// The workflow inbox messages.
    /// </summary>
    [Obsolete("Use BookmarkQueueItems instead.")]
    public DbSet<WorkflowInboxMessage> WorkflowInboxMessages { get; set; } = null!;

    #endregion
    
    /// <summary>
    /// The workflow triggers.
    /// </summary>
    public DbSet<StoredTrigger> Triggers { get; set; } = null!;
    
    /// <summary>
    /// The workflow execution log records.
    /// </summary>
    public DbSet<WorkflowExecutionLogRecord> WorkflowExecutionLogRecords { get; set; } = null!;
    
    /// <summary>
    /// The activity execution records.
    /// </summary>
    public DbSet<ActivityExecutionRecord> ActivityExecutionRecords { get; set; } = null!;
    
    /// <summary>
    /// The workflow bookmarks.
    /// </summary>
    public DbSet<StoredBookmark> Bookmarks { get; set; } = null!;
    
    /// <summary>
    /// The bookmark queue items.
    /// </summary>
    public DbSet<BookmarkQueueItem> BookmarkQueueItems { get; set; } = null!;

    /// <summary>
    /// The bookmark queue dead-letter items.
    /// </summary>
    public DbSet<BookmarkQueueDeadLetterItem> BookmarkQueueDeadLetterItems { get; set; } = null!;
    
    /// <summary>
    /// The generic key value pairs.
    /// </summary>
    public DbSet<SerializedKeyValuePair> KeyValuePairs { get; set; } = null!;

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var config = new Configurations();
        modelBuilder.ApplyConfiguration<StoredTrigger>(config);
        modelBuilder.ApplyConfiguration<WorkflowExecutionLogRecord>(config);
        modelBuilder.ApplyConfiguration<ActivityExecutionRecord>(config);
        modelBuilder.ApplyConfiguration<StoredBookmark>(config);
        modelBuilder.ApplyConfiguration<BookmarkQueueItem>(config);
        modelBuilder.ApplyConfiguration<BookmarkQueueDeadLetterItem>(config);
        modelBuilder.ApplyConfiguration<SerializedKeyValuePair>(config);
        modelBuilder.ApplyConfiguration<WorkflowInboxMessage>(config);
        
        base.OnModelCreating(modelBuilder);
    }
}
