using Flamme.Alterations.Core.Contracts;
using Flamme.Alterations.Core.Entities;
using Flamme.Alterations.Core.Extensions;
using Flamme.Alterations.Core.Models;
using Flamme.Alterations.Core.Stores;
using Flamme.Alterations.Extensions;
using Flamme.Alterations.Services;
using Flamme.Alterations.Workflows;
using Flamme.Extensions;
using Flamme.Features.Abstractions;
using Flamme.Features.Services;
using Flamme.Workflows.Options;
using Microsoft.Extensions.DependencyInjection;
using Flamme.Common.Serialization;

namespace Flamme.Alterations.Features;

/// <summary>
/// Adds the Flamme alterations services.
/// </summary>
public class AlterationsFeature : FeatureBase
{
    /// <inheritdoc />
    public AlterationsFeature(IModule module) : base(module)
    {
    }

    /// <summary>
    /// Gets or sets the factory for the alteration plan store.
    /// </summary>
    public Func<IServiceProvider, IAlterationPlanStore> AlterationPlanStoreFactory { get; set; } = sp => sp.GetRequiredService<MemoryAlterationPlanStore>();

    /// <summary>
    /// Gets or sets the factory for the alteration job store.
    /// </summary>
    public Func<IServiceProvider, IAlterationJobStore> AlterationJobStoreFactory { get; set; } = sp => sp.GetRequiredService<MemoryAlterationJobStore>();

    /// <summary>
    /// Gets or sets the factory for the alteration job dispatcher.
    /// </summary>
    public Func<IServiceProvider, IAlterationJobDispatcher> AlterationJobDispatcherFactory { get; set; } = sp => sp.GetRequiredService<BackgroundAlterationJobDispatcher>();

    /// <summary>
    /// Adds an alteration and its handler.
    /// </summary>
    /// <typeparam name="T">The type of alteration.</typeparam>
    /// <typeparam name="THandler">The type of alteration handler.</typeparam>
    public AlterationsFeature AddAlteration<T, THandler>() where T : class, IAlteration where THandler : class, IAlterationHandler
    {
        Services.AddAlteration<T, THandler>();
        return this;
    }

    /// <inheritdoc />
    public override void Configure()
    {
        Module.AddFastEndpointsAssembly<AlterationsFeature>();
        Module.AddWorkflow<ExecuteAlterationPlanWorkflow>();
    }

    /// <inheritdoc />
    public override void Apply()
    {
        Services.Configure<SerializationTypeOptions>(options =>
        {
            options.RegisterTypeAlias(typeof(AlterationPlanParams), nameof(AlterationPlanParams));
            options.RegisterLegacySimpleAssemblyQualifiedName(typeof(AlterationPlanParams));
        });

        Services.AddScoped<IAlterationPlanManager, AlterationPlanManager>();
        Services.AddAlterations();
        Services.AddAlterationsCore();
        Services.AddScoped<BackgroundAlterationJobDispatcher>();
        Services.AddScoped<IAlterationPlanScheduler, DefaultAlterationPlanScheduler>();
        Services.AddScoped<IAlterationJobRunner, DefaultAlterationJobRunner>();
        Services.AddScoped<IAlterationRunner, DefaultAlterationRunner>();

        Services.AddMemoryStore<AlterationPlan, MemoryAlterationPlanStore>();
        Services.AddMemoryStore<AlterationJob, MemoryAlterationJobStore>();

        Services.AddScoped(AlterationPlanStoreFactory);
        Services.AddScoped(AlterationJobStoreFactory);
        Services.AddScoped(AlterationJobDispatcherFactory);
    }
}
