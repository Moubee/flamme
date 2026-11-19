using Flamme.Features.Abstractions;
using Flamme.Features.Services;
using Flamme.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Flamme.UserTasks.Extensions;

namespace Flamme.UserTasks.Features;

public sealed class UserTasksFeature(IModule module) : FeatureBase(module)
{
    public Action<Options.UserTasksOptions>? ConfigureOptions { get; set; }

    public override void Configure()
    {
        Module.AddFastEndpointsAssembly<UserTasksFeature>();
    }

    public override void Apply()
    {
        Services.AddUserTasksServices(ConfigureOptions);
        Module.AddFastEndpointsFromModule();
    }
}
