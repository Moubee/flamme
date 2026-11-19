using Flamme.Extensions;
using Flamme.Workflows;
using Flamme.Workflows.Options;
using Flamme.Workflows.Runtime.ActivationValidators;
using Flamme.Workflows.Runtime.Bookmarks;
using Flamme.Workflows.Runtime.Stimuli;
using Flamme.Common.Serialization;

namespace Flamme.Workflows.Runtime;

internal static class WorkflowRuntimeTypeAliasRegistrar
{
    public static void Register(SerializationTypeOptions options, IEnumerable<Type> workflowTypes)
    {
        options.AddTypeAlias<EventBookmarkPayload>();
        options.AddTypeAlias<ExecuteWorkflowPayload>();
        options.AddTypeAlias<RunTaskBookmarkPayload>();
        options.AddTypeAlias<BookmarkTokenPayload>();
        options.AddTypeAlias<EventTokenPayload>();
        options.AddTypeAlias<ExecuteWorkflowResult>();
        options.AddTypeAlias<WorkflowInterruptedPayload>();
        options.AddTypeAlias<BackgroundActivityStimulus>();
        options.AddTypeAlias<BulkDispatchWorkflowsStimulus>();
        options.AddTypeAlias<DispatchWorkflowStimulus>();
        options.AddTypeAlias<EventStimulus>();
        options.AddTypeAlias<ExecuteWorkflowStimulus>();
        options.AddTypeAlias<RunTaskStimulus>();
        options.AddTypeAliasWithLegacyName<SingletonStrategy>(nameof(SingletonStrategy));
        options.AddTypeAliasWithLegacyName<CorrelatedSingletonStrategy>(nameof(CorrelatedSingletonStrategy));
        options.AddTypeAliasWithLegacyName<CorrelationStrategy>(nameof(CorrelationStrategy));

        foreach (var workflowType in workflowTypes.Where(IsConcreteWorkflowType).Distinct())
            options.RegisterTypeAlias(workflowType, workflowType.GetSimpleAssemblyQualifiedName());
    }

    private static bool IsConcreteWorkflowType(Type type)
    {
        return typeof(IWorkflow).IsAssignableFrom(type) && type is { IsAbstract: false, IsInterface: false, ContainsGenericParameters: false };
    }
}
