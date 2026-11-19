using Flamme.UserTasks.Features;
using Flamme.UserTasks.Persistence.EFCore.Features;

namespace Flamme.UserTasks.Persistence.EFCore.Extensions;

public static class UserTasksPersistenceFeatureExtensions
{
    public static UserTasksFeature UseEntityFrameworkCore(this UserTasksFeature feature, Action<EFCoreUserTasksPersistenceFeature>? configure = null)
    {
        feature.Module.Configure(configure);
        return feature;
    }
}
