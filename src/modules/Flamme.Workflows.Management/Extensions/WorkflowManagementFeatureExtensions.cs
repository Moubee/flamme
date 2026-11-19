using Flamme.Extensions;
using Flamme.Workflows.Management.Features;
using Flamme.Workflows.Options;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Flamme.Common.Serialization;

namespace Flamme.Workflows.Management;

[PublicAPI]
public static class WorkflowManagementFeatureExtensions
{
    public static WorkflowManagementFeature AddVariableTypeAndAlias<T>(this WorkflowManagementFeature management, string category)
    {
        return management.AddVariableTypeAndAlias<T>(typeof(T).Name, category);
    }
    
    public static WorkflowManagementFeature AddVariableTypeAndAlias<T>(this WorkflowManagementFeature management, string alias, string category)
    {
        management.AddVariableType<T>(category);
        management.Module.AddTypeAlias<T>(alias);
        management.Module.Services.Configure<SerializationTypeOptions>(options => options.RegisterTypeAlias(typeof(T), alias));
        return management;
    }
}
