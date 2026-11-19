using Flamme.Alterations.AlterationHandlers;
using Flamme.Alterations.AlterationTypes;
using Flamme.Alterations.Core.Extensions;
using Flamme.Alterations.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Flamme.Alterations.Extensions;

/// <summary>
/// Extension methods for <see cref="IServiceCollection"/>.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds various alteration handlers.
    /// </summary>
    public static IServiceCollection AddAlterations(this IServiceCollection services)
    {
        services.AddAlteration<Migrate, MigrateHandler>();
        services.AddAlteration<ModifyVariable, ModifyVariableHandler>();
        services.AddAlteration<ScheduleActivity, ScheduleActivityHandler>();
        services.AddAlteration<CancelActivity, CancelActivityHandler>();
        services.AddAlteration<Cancel, CancelHandler>();
        services.AddNotificationHandlersFrom<AlterationsFeature>();
        return services;
    }
}