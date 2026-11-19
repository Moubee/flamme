using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Flamme.Abstractions;
using Flamme.Extensions;
using Flamme.Models;
using Flamme.Workflows;
using Humanizer;
using Flamme.Common.Serialization;

namespace Flamme.Workflows.Api.Endpoints.WorkflowActivationStrategies.List;

/// <summary>
/// Returns list of available <see cref="IWorkflowActivationStrategy" /> implementations.
/// </summary>
internal class List(IEnumerable<IWorkflowActivationStrategy> strategies, ISerializationTypeRegistry workflowJsonTypeRegistry) : FlammeEndpointWithoutRequest<ListResponse<WorkflowActivationStrategyDescriptor>>
{
    public override void Configure()
    {
        Get("/descriptors/workflow-activation-strategies");

        // Static metadata about what is installed, which the designer needs to open any definition, so every signed-in user may read it.
        RequireAuthenticatedOnly();
    }

    public override Task<ListResponse<WorkflowActivationStrategyDescriptor>> ExecuteAsync(CancellationToken cancellationToken)
    {
        var descriptors = strategies.Select(x => WorkflowActivationStrategyDescriptor.FromStrategy(x, workflowJsonTypeRegistry)).OrderBy(x => x.DisplayName).ToList();
        var response = new ListResponse<WorkflowActivationStrategyDescriptor>(descriptors);
        return Task.FromResult(response);
    }
}

internal record WorkflowActivationStrategyDescriptor(string DisplayName, string Description, string TypeName)
{
    public static WorkflowActivationStrategyDescriptor FromStrategy(IWorkflowActivationStrategy strategy, ISerializationTypeRegistry workflowJsonTypeRegistry)
    {
        var type = strategy.GetType();
        var displayNameAttribute = type.GetCustomAttribute<DisplayNameAttribute>();
        var descriptionAttribute = type.GetCustomAttribute<DescriptionAttribute>();
        var displayAttribute = type.GetCustomAttribute<DisplayAttribute>();
        var displayName = displayNameAttribute?.DisplayName ?? displayAttribute?.Name ?? type.Name.Replace("Strategy", "").Humanize();
        var description = descriptionAttribute?.Description ?? displayAttribute?.Description ?? "";

        var typeName = workflowJsonTypeRegistry.TryGetAlias(type, out var alias) ? alias : type.GetSimpleAssemblyQualifiedName();
        return new WorkflowActivationStrategyDescriptor(displayName, description, typeName);
    }
}