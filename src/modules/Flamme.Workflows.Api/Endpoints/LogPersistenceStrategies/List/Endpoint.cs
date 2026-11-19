using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Flamme.Abstractions;
using Flamme.Extensions;
using Flamme.Models;
using Flamme.Workflows.LogPersistence;
using Humanizer;

namespace Flamme.Workflows.Api.Endpoints.LogPersistenceStrategies.List;

/// <summary>
/// Returns list of available <see cref="ILogPersistenceStrategy" /> implementations.
/// </summary>
internal class Endpoint(ILogPersistenceStrategyService logPersistenceStrategyService) : FlammeEndpointWithoutRequest<ListResponse<LogPersistenceStrategyDescriptor>>
{
    public override void Configure()
    {
        Get("/descriptors/log-persistence-strategies");

        // Static metadata about what is installed, which the designer needs to open any definition, so every signed-in user may read it.
        RequireAuthenticatedOnly();
    }

    public override Task<ListResponse<LogPersistenceStrategyDescriptor>> ExecuteAsync(CancellationToken cancellationToken)
    {
        var strategies = logPersistenceStrategyService.ListStrategies();
        var descriptors = strategies.Select(LogPersistenceStrategyDescriptor.FromStrategy).OrderBy(x => x.DisplayName).ToList();
        var response =new ListResponse<LogPersistenceStrategyDescriptor>(descriptors);
        return Task.FromResult(response);
    }
}

internal record LogPersistenceStrategyDescriptor(string DisplayName, string Description, string TypeName)
{
    public static LogPersistenceStrategyDescriptor FromStrategy(ILogPersistenceStrategy strategy)
    {
        var type = strategy.GetType();
        var displayNameAttribute = type.GetCustomAttribute<DisplayNameAttribute>();
        var descriptionAttribute = type.GetCustomAttribute<DescriptionAttribute>();
        var displayAttribute = type.GetCustomAttribute<DisplayAttribute>();
        var displayName = displayNameAttribute?.DisplayName ?? displayAttribute?.Name ?? type.Name.Replace("Strategy", "").Humanize();
        var description = descriptionAttribute?.Description ?? displayAttribute?.Description ?? "";

        return new LogPersistenceStrategyDescriptor(displayName, description, type.GetSimpleAssemblyQualifiedName());
    }
}