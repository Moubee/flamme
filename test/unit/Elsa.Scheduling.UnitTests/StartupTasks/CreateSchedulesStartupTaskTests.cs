using Elsa.Common;
using Elsa.Common.Multitenancy;
using Elsa.Common.Models;
using Elsa.Scheduling.Options;
using Elsa.Scheduling.StartupTasks;
using Elsa.Workflows.Management;
using Elsa.Workflows.Management.Filters;
using Elsa.Workflows.Runtime;
using Elsa.Workflows.Runtime.Entities;
using Elsa.Workflows.Runtime.Filters;
using Elsa.Workflows.Runtime.Tasks;
using Microsoft.Extensions.DependencyInjection;
using OptionsFactory = Microsoft.Extensions.Options.Options;

namespace Elsa.Scheduling.UnitTests.StartupTasks;

public class CreateSchedulesStartupTaskTests
{
    private readonly StoredTrigger[] _triggers =
    [
        new() { Id = "trigger-1", WorkflowDefinitionId = "definition", WorkflowDefinitionVersionId = "version", ActivityId = "activity" }
    ];

    private readonly StoredBookmark[] _bookmarks = [new() { Id = "bookmark-1", Hash = "hash", WorkflowInstanceId = "instance" }];
    private readonly ITriggerStore _triggerStore = ITriggerStore.Mock().Object;
    private readonly IBookmarkStore _bookmarkStore = IBookmarkStore.Mock().Object;
    private readonly ITriggerScheduler _triggerScheduler = ITriggerScheduler.Mock().Object;
    private readonly IBookmarkScheduler _bookmarkScheduler = IBookmarkScheduler.Mock().Object;
    private readonly IWorkflowInstanceStore _workflowInstanceStore = IWorkflowInstanceStore.Mock().Object;
    private readonly IBookmarkManager _bookmarkManager = IBookmarkManager.Mock().Object;
    private readonly SchedulingOptions _options = new() { StartupSchedulePageSize = 1000 };

    public CreateSchedulesStartupTaskTests()
    {
        Mock.Get(_triggerStore).FindManyAsync(Arg.Any<TriggerFilter>(), Arg.Any<PageArgs>(), Arg.Any<CancellationToken>())
            .Returns(new Page<StoredTrigger>(_triggers, _triggers.Length));
        Mock.Get(_bookmarkStore).FindManyAsync(Arg.Any<BookmarkFilter>(), Arg.Any<PageArgs>(), Arg.Any<CancellationToken>())
            .Returns(new Page<StoredBookmark>(_bookmarks, _bookmarks.Length));
        Mock.Get(_workflowInstanceStore).FindManyIdsAsync(Arg.Any<WorkflowInstanceFilter>(), Arg.Any<CancellationToken>())
            .Returns((WorkflowInstanceFilter filter, CancellationToken _) => RunningIds(filter));
    }

    [Test]
    public async Task Task_DependsOnPopulateRegistriesStartupTask()
    {
        var dependency = await Assert.That(typeof(CreateSchedulesStartupTask).GetCustomAttributes(typeof(TaskDependencyAttribute), false).Cast<TaskDependencyAttribute>()).HasSingleItem();

        await Assert.That(dependency.DependencyTaskType).IsEqualTo(typeof(PopulateRegistriesStartupTask));
    }

    [Test]
    public async Task ExecuteAsync_WithoutTenantBackgroundQueue_SchedulesImmediately()
    {
        var task = new CreateSchedulesStartupTask(CreateServiceProvider(), OptionsFactory.Create(_options));

        await task.ExecuteAsync(CancellationToken.None);

        Mock.Get(_triggerScheduler).ScheduleAsync(Arg.Is<IEnumerable<StoredTrigger>>(x => x.SequenceEqual(_triggers)), Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
        Mock.Get(_bookmarkScheduler).ScheduleAsync(Arg.Is<IEnumerable<StoredBookmark>>(x => x.SequenceEqual(_bookmarks)), Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
    }

    [Test]
    public async Task ExecuteAsync_WithTenantBackgroundQueue_EnqueuesScheduleCreation()
    {
        TenantBackgroundWorkItem? workItem = null;
        var workQueue = ITenantBackgroundWorkQueue.Mock().Object;
        Mock.Get(workQueue).EnqueueAsync(Arg.Any<TenantBackgroundWorkItem>(), Arg.Any<CancellationToken>())
            .Callback((TenantBackgroundWorkItem item, CancellationToken _) => workItem = item);
        var serviceProvider = CreateServiceProvider(services => services.AddSingleton(workQueue));
        var task = new CreateSchedulesStartupTask(serviceProvider, OptionsFactory.Create(_options));

        await task.ExecuteAsync(CancellationToken.None);

        Mock.Get(workQueue).EnqueueAsync(Arg.Any<TenantBackgroundWorkItem>(), Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
        Mock.Get(_triggerScheduler).ScheduleAsync(Arg.Any<IEnumerable<StoredTrigger>>(), Arg.Any<CancellationToken>()).WasNeverCalled();

        await Assert.That(workItem).IsNotNull();
        await workItem(serviceProvider, CancellationToken.None);

        Mock.Get(_triggerScheduler).ScheduleAsync(Arg.Is<IEnumerable<StoredTrigger>>(x => x.SequenceEqual(_triggers)), Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
        Mock.Get(_bookmarkScheduler).ScheduleAsync(Arg.Is<IEnumerable<StoredBookmark>>(x => x.SequenceEqual(_bookmarks)), Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
    }

    [Test]
    public async Task ExecuteAsync_SchedulesInConfiguredPages()
    {
        var firstTriggerPage = new[] { _triggers[0] };
        var secondTriggerPage = new[] { new StoredTrigger { Id = "trigger-2", WorkflowDefinitionId = "definition", WorkflowDefinitionVersionId = "version", ActivityId = "activity" } };
        var firstBookmarkPage = new[] { _bookmarks[0] };
        var secondBookmarkPage = new[] { new StoredBookmark { Id = "bookmark-2", Hash = "hash", WorkflowInstanceId = "instance" } };
        _options.StartupSchedulePageSize = 1;
        Mock.Get(_triggerStore).FindManyAsync(Arg.Any<TriggerFilter>(), Arg.Is<PageArgs>(x => x.Offset == 0 && x.Limit == 1), Arg.Any<CancellationToken>())
            .Returns(new Page<StoredTrigger>(firstTriggerPage, 2));
        Mock.Get(_triggerStore).FindManyAsync(Arg.Any<TriggerFilter>(), Arg.Is<PageArgs>(x => x.Offset == 1 && x.Limit == 1), Arg.Any<CancellationToken>())
            .Returns(new Page<StoredTrigger>(secondTriggerPage, 2));
        Mock.Get(_bookmarkStore).FindManyAsync(Arg.Any<BookmarkFilter>(), Arg.Is<PageArgs>(x => x.Offset == 0 && x.Limit == 1), Arg.Any<CancellationToken>())
            .Returns(new Page<StoredBookmark>(firstBookmarkPage, 2));
        Mock.Get(_bookmarkStore).FindManyAsync(Arg.Any<BookmarkFilter>(), Arg.Is<PageArgs>(x => x.Offset == 1 && x.Limit == 1), Arg.Any<CancellationToken>())
            .Returns(new Page<StoredBookmark>(secondBookmarkPage, 2));
        var task = new CreateSchedulesStartupTask(CreateServiceProvider(), OptionsFactory.Create(_options));

        await task.ExecuteAsync(CancellationToken.None);

        Mock.Get(_triggerScheduler).ScheduleAsync(Arg.Is<IEnumerable<StoredTrigger>>(x => x.SequenceEqual(firstTriggerPage)), Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
        Mock.Get(_triggerScheduler).ScheduleAsync(Arg.Is<IEnumerable<StoredTrigger>>(x => x.SequenceEqual(secondTriggerPage)), Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
        Mock.Get(_bookmarkScheduler).ScheduleAsync(Arg.Is<IEnumerable<StoredBookmark>>(x => x.SequenceEqual(firstBookmarkPage)), Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
        Mock.Get(_bookmarkScheduler).ScheduleAsync(Arg.Is<IEnumerable<StoredBookmark>>(x => x.SequenceEqual(secondBookmarkPage)), Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
    }

    [Test]
    public async Task ExecuteAsync_SkipsAndPurgesOrphanSchedulingBookmarks()
    {
        var missingInstance = Bookmark("missing-bookmark", "missing-instance");
        var finishedInstance = Bookmark("finished-bookmark", "finished-instance");
        var cancelledInstance = Bookmark("cancelled-bookmark", "cancelled-instance");
        var faultedInstance = Bookmark("faulted-bookmark", "faulted-instance");
        var emptyInstanceId = Bookmark("empty-instance-bookmark", "");
        var suspendedInstance = Bookmark("suspended-bookmark", "suspended-instance");
        var bookmarks = new[]
        {
            missingInstance, finishedInstance, cancelledInstance, faultedInstance, emptyInstanceId, suspendedInstance
        };
        BookmarkFilter? purgedFilter = null;
        Mock.Get(_bookmarkStore).FindManyAsync(Arg.Any<BookmarkFilter>(), Arg.Any<PageArgs>(), Arg.Any<CancellationToken>())
            .Returns(new Page<StoredBookmark>(bookmarks, bookmarks.Length));
        StubBookmarkReload(bookmarks);
        Mock.Get(_workflowInstanceStore).FindManyIdsAsync(Arg.Any<WorkflowInstanceFilter>(), Arg.Any<CancellationToken>())
            .Returns(["suspended-instance"]);
        Mock.Get(_bookmarkManager).DeleteManyAsync(Arg.Any<BookmarkFilter>(), Arg.Any<CancellationToken>())
            .Callback((BookmarkFilter filter, CancellationToken _) => purgedFilter = filter)
            .Returns(5);
        var task = new CreateSchedulesStartupTask(CreateServiceProvider(), OptionsFactory.Create(_options));

        await task.ExecuteAsync(CancellationToken.None);

        Mock.Get(_bookmarkScheduler).ScheduleAsync(
            Arg.Is<IEnumerable<StoredBookmark>>(x => x.SequenceEqual(new[] { suspendedInstance })),
            Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
        Mock.Get(_bookmarkManager).DeleteManyAsync(Arg.Any<BookmarkFilter>(), Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
        await Assert.That(purgedFilter).IsNotNull();
        await Assert.That(purgedFilter.BookmarkIds?.OrderBy(x => x, StringComparer.Ordinal).ToArray()).IsEquivalentTo(new[]
            {
                "cancelled-bookmark", "empty-instance-bookmark", "faulted-bookmark", "finished-bookmark", "missing-bookmark"
            }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task ExecuteAsync_SchedulesOrphanWhoseInstanceBecomesRunningBeforePurge()
    {
        var revived = Bookmark("revived-bookmark", "revived-instance");
        var stillMissing = Bookmark("missing-bookmark", "missing-instance");
        var bookmarks = new[] { revived, stillMissing };
        BookmarkFilter? purgedFilter = null;
        Mock.Get(_bookmarkStore).FindManyAsync(Arg.Any<BookmarkFilter>(), Arg.Any<PageArgs>(), Arg.Any<CancellationToken>())
            .Returns(new Page<StoredBookmark>(bookmarks, bookmarks.Length));
        StubBookmarkReload(bookmarks);
        Mock.Get(_workflowInstanceStore).FindManyIdsAsync(Arg.Any<WorkflowInstanceFilter>(), Arg.Any<CancellationToken>())
            .ReturnsSequentially(Array.Empty<string>(), new[] { "revived-instance" });
        Mock.Get(_bookmarkManager).DeleteManyAsync(Arg.Any<BookmarkFilter>(), Arg.Any<CancellationToken>())
            .Callback((BookmarkFilter filter, CancellationToken _) => purgedFilter = filter)
            .Returns(1);
        var task = new CreateSchedulesStartupTask(CreateServiceProvider(), OptionsFactory.Create(_options));

        await task.ExecuteAsync(CancellationToken.None);

        Mock.Get(_bookmarkScheduler).ScheduleAsync(
            Arg.Is<IEnumerable<StoredBookmark>>(x => x.SequenceEqual(new[] { revived })),
            Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
        Mock.Get(_bookmarkManager).DeleteManyAsync(Arg.Any<BookmarkFilter>(), Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
        await Assert.That(purgedFilter).IsNotNull();
        await Assert.That(purgedFilter.BookmarkIds).IsEquivalentTo(["missing-bookmark"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task ExecuteAsync_ReconcilesOrphansInConfiguredPages()
    {
        var firstOrphan = Bookmark("orphan-1", "missing-1");
        var secondOrphan = Bookmark("orphan-2", "missing-2");
        var bookmarks = new[] { firstOrphan, secondOrphan };
        var purgedFilters = new List<BookmarkFilter>();
        var reloadedIdBatches = new List<ICollection<string>>();
        _options.StartupSchedulePageSize = 1;
        Mock.Get(_bookmarkStore).FindManyAsync(Arg.Any<BookmarkFilter>(), Arg.Is<PageArgs>(x => x.Offset == 0 && x.Limit == 1), Arg.Any<CancellationToken>())
            .Returns(new Page<StoredBookmark>([firstOrphan], 2));
        Mock.Get(_bookmarkStore).FindManyAsync(Arg.Any<BookmarkFilter>(), Arg.Is<PageArgs>(x => x.Offset == 1 && x.Limit == 1), Arg.Any<CancellationToken>())
            .Returns(new Page<StoredBookmark>([secondOrphan], 2));
        Mock.Get(_bookmarkStore).FindManyAsync(Arg.Any<BookmarkFilter>(), Arg.Any<CancellationToken>())
            .Returns((BookmarkFilter filter, CancellationToken _) =>
            {
                var ids = filter.BookmarkIds ?? [];
                reloadedIdBatches.Add(ids.ToList());
                return bookmarks.Where(x => ids.Contains(x.Id));
            });
        Mock.Get(_workflowInstanceStore).FindManyIdsAsync(Arg.Any<WorkflowInstanceFilter>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<string>());
        Mock.Get(_bookmarkManager).DeleteManyAsync(Arg.Any<BookmarkFilter>(), Arg.Any<CancellationToken>())
            .Callback((BookmarkFilter filter, CancellationToken _) => purgedFilters.Add(filter))
            .Returns(1);
        var task = new CreateSchedulesStartupTask(CreateServiceProvider(), OptionsFactory.Create(_options));

        await task.ExecuteAsync(CancellationToken.None);

        Mock.Get(_bookmarkScheduler).ScheduleAsync(Arg.Any<IEnumerable<StoredBookmark>>(), Arg.Any<CancellationToken>()).WasNeverCalled();
        await Assert.That(reloadedIdBatches.Count).IsEqualTo(2);
        foreach (var batch in reloadedIdBatches)
        {
            await Assert.That(batch).HasSingleItem();
        }
        await Assert.That(purgedFilters.Count).IsEqualTo(2);
        foreach (var filter in purgedFilters)
        {
            await Assert.That(filter.BookmarkIds!).HasSingleItem();
        }
        await Assert.That(purgedFilters.SelectMany(x => x.BookmarkIds!).OrderBy(x => x, StringComparer.Ordinal)).IsEquivalentTo(["orphan-1", "orphan-2"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task ExecuteAsync_WithoutWorkflowInstanceStore_SchedulesAllBookmarks()
    {
        var missingInstance = Bookmark("missing-bookmark", "missing-instance");
        var bookmarks = new[] { missingInstance, _bookmarks[0] };
        Mock.Get(_bookmarkStore).FindManyAsync(Arg.Any<BookmarkFilter>(), Arg.Any<PageArgs>(), Arg.Any<CancellationToken>())
            .Returns(new Page<StoredBookmark>(bookmarks, bookmarks.Length));
        var task = new CreateSchedulesStartupTask(CreateServiceProvider(registerReconcileServices: false), OptionsFactory.Create(_options));

        await task.ExecuteAsync(CancellationToken.None);

        Mock.Get(_bookmarkScheduler).ScheduleAsync(Arg.Is<IEnumerable<StoredBookmark>>(x => x.SequenceEqual(bookmarks)), Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
        Mock.Get(_bookmarkManager).DeleteManyAsync(Arg.Any<BookmarkFilter>(), Arg.Any<CancellationToken>()).WasNeverCalled();
    }

    [Test]
    public async Task ExecuteAsync_WithoutBookmarkManager_SkipsOrphansButDoesNotPurge()
    {
        var missingInstance = Bookmark("missing-bookmark", "missing-instance");
        var suspendedInstance = Bookmark("suspended-bookmark", "suspended-instance");
        var bookmarks = new[] { missingInstance, suspendedInstance };
        Mock.Get(_bookmarkStore).FindManyAsync(Arg.Any<BookmarkFilter>(), Arg.Any<PageArgs>(), Arg.Any<CancellationToken>())
            .Returns(new Page<StoredBookmark>(bookmarks, bookmarks.Length));
        StubBookmarkReload(bookmarks);
        Mock.Get(_workflowInstanceStore).FindManyIdsAsync(Arg.Any<WorkflowInstanceFilter>(), Arg.Any<CancellationToken>())
            .Returns(["suspended-instance"]);
        var task = new CreateSchedulesStartupTask(
            CreateServiceProvider(services => services.AddSingleton(_workflowInstanceStore), registerReconcileServices: false),
            OptionsFactory.Create(_options));

        await task.ExecuteAsync(CancellationToken.None);

        Mock.Get(_bookmarkScheduler).ScheduleAsync(
            Arg.Is<IEnumerable<StoredBookmark>>(x => x.SequenceEqual(new[] { suspendedInstance })),
            Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
        Mock.Get(_bookmarkManager).DeleteManyAsync(Arg.Any<BookmarkFilter>(), Arg.Any<CancellationToken>()).WasNeverCalled();
    }

    [Test]
    public async Task ExecuteAsync_DoesNotPurgeWhenEverySchedulingBookmarkIsLive()
    {
        var task = new CreateSchedulesStartupTask(CreateServiceProvider(), OptionsFactory.Create(_options));

        await task.ExecuteAsync(CancellationToken.None);

        Mock.Get(_bookmarkManager).DeleteManyAsync(Arg.Any<BookmarkFilter>(), Arg.Any<CancellationToken>()).WasNeverCalled();
    }

    private ServiceProvider CreateServiceProvider(Action<IServiceCollection>? configureServices = null, bool registerReconcileServices = true)
    {
        var services = new ServiceCollection();
        services.AddSingleton(_triggerStore);
        services.AddSingleton(_bookmarkStore);
        services.AddSingleton(_triggerScheduler);
        services.AddSingleton(_bookmarkScheduler);

        if (registerReconcileServices)
        {
            services.AddSingleton(_workflowInstanceStore);
            services.AddSingleton(_bookmarkManager);
        }

        configureServices?.Invoke(services);
        return services.BuildServiceProvider();
    }

    private void StubBookmarkReload(IEnumerable<StoredBookmark> bookmarks)
    {
        var bookmarkList = bookmarks.ToList();
        Mock.Get(_bookmarkStore).FindManyAsync(Arg.Any<BookmarkFilter>(), Arg.Any<CancellationToken>())
            .Returns((BookmarkFilter filter, CancellationToken _) =>
            {
                var ids = filter.BookmarkIds;
                if (ids == null)
                    return bookmarkList.AsEnumerable();

                var idSet = ids.ToHashSet(StringComparer.Ordinal);
                return bookmarkList.Where(x => idSet.Contains(x.Id));
            });
    }

    private static IEnumerable<string> RunningIds(WorkflowInstanceFilter filter)
    {
        return (filter.Ids ?? []).Where(id => !string.IsNullOrWhiteSpace(id));
    }

    private static StoredBookmark Bookmark(string id, string workflowInstanceId) => new()
    {
        Id = id,
        Hash = "hash",
        Name = SchedulingStimulusNames.Delay,
        WorkflowInstanceId = workflowInstanceId
    };
}
