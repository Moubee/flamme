using Flamme.Alterations.Core.Contracts;
using Flamme.Alterations.Core.Options;
using Flamme.Alterations.Core.Serialization;
using Flamme.Alterations.Core.Services;
using Flamme.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Flamme.Alterations.Core.Extensions;

/// <summary>
/// Extension methods for <see cref="IServiceCollection"/>.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds the core Flamme alterations services.
    /// </summary>
    public static IServiceCollection AddAlterationsCore(this IServiceCollection services)
    {
        services.Configure<AlterationOptions>(_ => { }); // Ensure that the options are configured even if the application doesn't do so.
        services.AddScoped<IAlteredWorkflowDispatcher, AlteredWorkflowDispatcher>();
        services.AddScoped<IWorkflowInstanceFinder, WorkflowInstanceFinder>();
        services.AddSingleton<IAlterationSerializer, AlterationSerializer>();
        services.AddSerializationOptionsConfigurator<AlterationSerializationOptionConfigurator>();
        return services;
    }

    /// <summary>
    /// Adds an alteration handler.
    /// </summary>
    public static IServiceCollection AddAlteration<T, THandler>(this IServiceCollection services) where T : IAlteration where THandler : class, IAlterationHandler
    {
        services.Configure<AlterationOptions>(options => options.AlterationTypes.Add(typeof(T)));
        services.AddScoped<IAlterationHandler, THandler>();
        return services;
    }
}