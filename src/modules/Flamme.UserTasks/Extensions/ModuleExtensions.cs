using Flamme.Features.Services;
using Flamme.UserTasks.Features;

// ReSharper disable once CheckNamespace
namespace Flamme.Extensions;

public static class UserTasksModuleExtensions
{
    public static IModule UseUserTasks(this IModule module, Action<UserTasksFeature>? configure = null) => module.Use(configure);
}
