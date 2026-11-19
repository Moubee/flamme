using Flamme.Common.Entities;
using Flamme.Common.Multitenancy;
using Flamme.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.DependencyInjection;

namespace Flamme.Persistence.EFCore;

/// <summary>
/// An optional base class to implement with some opinions on certain converters to install for certain DB providers.
/// </summary>
public abstract class FlammeDbContextBase : DbContext, IFlammeDbContextSchema
{
    private static readonly ISet<EntityState> ModifiedEntityStates = new HashSet<EntityState>
    {
        EntityState.Added,
        EntityState.Modified,
    };

    protected IServiceProvider ServiceProvider { get; }
    private readonly FlammeDbContextOptions? _flammeDbContextOptions;
    public string? TenantId { get; set; }

    /// <summary>
    /// The default schema used by Flamme.
    /// </summary>
    public static string FlammeSchema { get; set; } = "Elsa";

    /// <inheritdoc/>
    public string Schema { get; }

    /// <summary>
    /// The table used to store the migrations history.
    /// </summary>
    public static string MigrationsHistoryTable { get; set; } = "__EFMigrationsHistory";

    /// <summary>
    /// Initializes a new instance of the <see cref="FlammeDbContextBase"/> class.
    /// </summary>
    protected FlammeDbContextBase(DbContextOptions options, IServiceProvider serviceProvider) : base(options)
    {
        ServiceProvider = serviceProvider;
        _flammeDbContextOptions = options.FindExtension<FlammeDbContextOptionsExtension>()?.Options;

        // ReSharper disable once VirtualMemberCallInConstructor
        Schema = !string.IsNullOrWhiteSpace(_flammeDbContextOptions?.SchemaName) ? _flammeDbContextOptions.SchemaName : FlammeSchema;

        var tenantAccessor = serviceProvider.GetService<ITenantAccessor>();
        var tenantId = (tenantAccessor?.TenantId).NormalizeTenantId();
        TenantId ??= tenantId;
    }

    /// <inheritdoc/>
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await OnBeforeSavingAsync(cancellationToken);
        return await base.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        if (!string.IsNullOrWhiteSpace(Schema))
            modelBuilder.HasDefaultSchema(Schema);

        var additionalConfigurations = _flammeDbContextOptions?.GetModelConfigurations(this);

        additionalConfigurations?.Invoke(modelBuilder);

        using var scope = ServiceProvider.CreateScope();
        var entityTypeHandlers = scope.ServiceProvider.GetServices<IEntityModelCreatingHandler>().ToList();

        foreach (var entityType in modelBuilder.Model.GetEntityTypes().ToList())
        {
            foreach (var handler in entityTypeHandlers)
                handler.Handle(this, modelBuilder, entityType);
        }
    }

    private async Task OnBeforeSavingAsync(CancellationToken cancellationToken)
    {
        using var scope = ServiceProvider.CreateScope();
        var handlers = scope.ServiceProvider.GetServices<IEntitySavingHandler>().ToList();
        foreach (var entry in ChangeTracker.Entries().Where(IsModifiedEntity))
        {
            foreach (var handler in handlers)
                await handler.HandleAsync(this, entry, cancellationToken);
        }
    }

    /// <summary>
    /// Determine if an entity was modified.
    /// </summary>
    private bool IsModifiedEntity(EntityEntry entityEntry)
    {
        return ModifiedEntityStates.Contains(entityEntry.State) && entityEntry.Entity is Entity;
    }
}