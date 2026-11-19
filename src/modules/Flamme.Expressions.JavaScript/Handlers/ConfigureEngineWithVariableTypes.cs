using Flamme.Expressions.JavaScript.Notifications;
using Flamme.Extensions;
using Flamme.Mediator.Contracts;
using Flamme.Workflows.Management.Options;
using JetBrains.Annotations;
using Microsoft.Extensions.Options;

namespace Flamme.Expressions.JavaScript.Handlers;

[UsedImplicitly]
public class ConfigureEngineWithWorkflowVariableTypes(IOptions<ManagementOptions> options)
    : INotificationHandler<CreatingJavaScriptEngine>
{
    private static readonly Type[] BlacklistedTypes =
    [
        typeof(string),
        typeof(object),
        // Add more types if needed.
    ];

    /// <inheritdoc />
    public Task HandleAsync(CreatingJavaScriptEngine notification, CancellationToken cancellationToken)
    {
        var engineOptions = notification.Options;
        var variableTypes = options.Value.VariableDescriptors
            .Where(x => x.Type is { ContainsGenericParameters: false } && !BlacklistedTypes.Contains(x.Type) && !x.Type.IsPrimitive)
            .Select(x => x.Type);

        foreach (var variableType in variableTypes)
        {
            engineOptions.RegisterType(variableType);
        }

        return Task.CompletedTask;
    }
}
