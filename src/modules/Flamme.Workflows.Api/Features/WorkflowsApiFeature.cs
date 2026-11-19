using Flamme.Extensions;
using Flamme.Features.Abstractions;
using Flamme.Features.Attributes;
using Flamme.Features.Services;
using Flamme.SasTokens.Features;
using Flamme.Workflows.Api.Constants;
using Flamme.Workflows.Api.Requirements;
using Flamme.Workflows.Api.Serialization;
using Flamme.Workflows.Api.Security;
using Flamme.Workflows.Api.Services;
using Flamme.Workflows.Management.Features;
using Flamme.Workflows.Runtime.Features;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace Flamme.Workflows.Api.Features;

/// <summary>
/// Adds workflows API features.
/// </summary>
[DependsOn(typeof(WorkflowInstancesFeature))]
[DependsOn(typeof(WorkflowManagementFeature))]
[DependsOn(typeof(WorkflowRuntimeFeature))]
[DependsOn(typeof(SasTokensFeature))]
public class WorkflowsApiFeature(IModule module) : FeatureBase(module)
{
    /// <inheritdoc />
    public override void Configure()
    {
        Module.AddFastEndpointsAssembly(GetType());
    }

    /// <inheritdoc />
    public override void Apply()
    {
        Services.AddSerializationOptionsConfigurator<SerializationConfigurator>();
        Module.AddFastEndpointsFromModule();

        Services.AddScoped<IWorkflowDefinitionLinker, StaticWorkflowDefinitionLinker>();
        Services.AddScoped<WorkflowDefinitionScriptAuthorizationService>();
        Services.AddScoped<IAuthorizationHandler, NotReadOnlyRequirementHandler>();
        Services.Configure<AuthorizationOptions>(options =>
        {
            options.AddPolicy(AuthorizationPolicies.NotReadOnlyPolicy, policy => policy.AddRequirements(new NotReadOnlyRequirement()));
        });
        Services.AddScoped<IWorkflowInstanceExportNameProvider, DefaultWorkflowInstanceExportNameProvider>();
    }
}
