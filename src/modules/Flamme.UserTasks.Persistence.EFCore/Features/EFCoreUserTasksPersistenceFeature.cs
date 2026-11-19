using Flamme.Features.Attributes;
using Flamme.Features.Services;
using Flamme.Persistence.EFCore;
using Flamme.UserTasks.Contracts;
using Flamme.UserTasks.Features;
using Flamme.UserTasks.Persistence.EFCore.Contracts;
using Flamme.UserTasks.Persistence.EFCore.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace Flamme.UserTasks.Persistence.EFCore.Features;

/// <summary>
/// Configures the identity-neutral User Tasks repository with EF Core persistence.
/// </summary>
[DependsOn(typeof(UserTasksFeature))]
public class EFCoreUserTasksPersistenceFeature(IModule module)
    : PersistenceFeatureBase<EFCoreUserTasksPersistenceFeature, UserTasksFlammeDbContext>(module)
{
    public override void Apply()
    {
        base.Apply();
        AddStore<UserTaskRecord, EFCoreUserTaskRepository>();
        AddStore<UserTaskGuestSessionRecord, EFCoreUserTaskGuestSessionIssuer>();
        AddStore<UserTaskInvitationDeliveryRecord, EFCoreUserTaskInvitationOutbox>();
        Services.AddScoped<IUserTaskRepository, EFCoreUserTaskRepository>();
        Services.AddScoped<IUserTaskPersistenceAdapter, EFCoreUserTaskRepository>();

        // Guest sessions and invitation deliveries carry secrets that must survive a restart and a
        // failover, so the provider replaces the Core in-memory defaults registered by TryAdd.
        Services.AddScoped<IUserTaskGuestSessionIssuer, EFCoreUserTaskGuestSessionIssuer>();
        Services.AddScoped<IUserTaskInvitationOutbox, EFCoreUserTaskInvitationOutbox>();
    }
}
