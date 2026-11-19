using Flamme.Common.Multitenancy;
using Flamme.Scheduling.Activities;
using Flamme.Workflows.Helpers;
using Flamme.Workflows.Runtime;
using Flamme.Workflows.Runtime.Entities;
using Flamme.Workflows.Runtime.Filters;
using Microsoft.Extensions.DependencyInjection;
using Timer = Flamme.Scheduling.Activities.Timer;

namespace Flamme.Scheduling.Handlers;

public class UpdateTenantSchedules : ITenantDeletedEvent
{
    private static readonly string[] ActivityTypeNames =
    [
        ActivityTypeNameHelper.GenerateTypeName<Cron>(),
        ActivityTypeNameHelper.GenerateTypeName<Timer>(),
        ActivityTypeNameHelper.GenerateTypeName<Delay>()
    ];

    public async Task TenantDeletedAsync(TenantDeletedEventArgs args)
    {
        var serviceProvider = args.TenantScope.ServiceProvider;
        var cancellationToken = args.CancellationToken;
        var triggerScheduler = args.TenantScope.ServiceProvider.GetRequiredService<ITriggerScheduler>();
        var bookmarkScheduler = args.TenantScope.ServiceProvider.GetRequiredService<IBookmarkScheduler>();
        var triggers = (await GetTriggersAsync(serviceProvider, cancellationToken)).ToList();
        var bookmarks = (await GetBookmarksAsync(serviceProvider, cancellationToken)).ToList();

        await triggerScheduler.UnscheduleAsync(triggers, args.CancellationToken);
        await bookmarkScheduler.UnscheduleAsync(bookmarks, args.CancellationToken);
    }

    private async Task<IEnumerable<StoredTrigger>> GetTriggersAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        var triggerStore = serviceProvider.GetRequiredService<ITriggerStore>();
        var filter = TriggerFilter.ByNames(ActivityTypeNames);
        return await triggerStore.FindManyAsync(filter, cancellationToken);
    }

    private async Task<IEnumerable<StoredBookmark>> GetBookmarksAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        var bookmarkStore = serviceProvider.GetRequiredService<IBookmarkStore>();
        var filter = BookmarkFilter.ByActivityTypeNames(ActivityTypeNames);
        return await bookmarkStore.FindManyAsync(filter, cancellationToken);
    }
}
