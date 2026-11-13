using Elsa.Common;
using Elsa.Common.Multitenancy;
using Elsa.Common.Services;
using Elsa.Workflows.Runtime.Entities;
using Elsa.Workflows.Runtime.Filters;
using Elsa.Workflows.Runtime.Messages;
using Elsa.Workflows.Runtime.Options;
using Elsa.Workflows.Runtime.Stores;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Elsa.Workflows.Runtime.UnitTests.Services;

public class BookmarkQueueTenantIsolationTests
{
    [Test]
    public async Task TwoTenants_WithQueuedBookmarks_BothResume()
    {
        var accessor = new DefaultTenantAccessor();
        var signaler = new BookmarkQueueSignaler(accessor);
        var tenantA = Tenant("tenant-a");
        var tenantB = Tenant("tenant-b");
        var resumed = new List<string>();
        var tenantsProvider = Substitute.For<ITenantsProvider>();

        var (workerA, processorA) = CreateResumingWorker(signaler, accessor, tenantA, "bookmark-a", resumed, tenantsProvider);
        var (workerB, processorB) = CreateResumingWorker(signaler, accessor, tenantB, "bookmark-b", resumed, tenantsProvider);

        try
        {
            using (accessor.PushContext(tenantA))
                workerA.Start();
            using (accessor.PushContext(tenantB))
                workerB.Start();

            using (accessor.PushContext(tenantA))
                await signaler.TriggerAsync();
            using (accessor.PushContext(tenantB))
                await signaler.TriggerAsync();

            await WaitUntilAsync(() => resumed.Count >= 2);

            await Assert.That(resumed).Contains("bookmark-a");
            await Assert.That(resumed).Contains("bookmark-b");
            await Assert.That(processorA.AmbientTenantIds).IsEquivalentTo(new string?[] { "tenant-a" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
            await Assert.That(processorB.AmbientTenantIds).IsEquivalentTo(["tenant-b"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
            await tenantsProvider.DidNotReceive().ListAsync(Arg.Any<CancellationToken>());
        }
        finally
        {
            workerA.Stop();
            workerB.Stop();
        }
    }

    [Test]
    public async Task EmptyQueue_DoesNotListTenants_OrOpenTenantScopes()
    {
        var accessor = new DefaultTenantAccessor();
        var signaler = new BookmarkQueueSignaler(accessor);
        var tenantsProvider = Substitute.For<ITenantsProvider>();
        var (worker, scopes) = CreateRecordingWorker(signaler, accessor, Tenant("tenant-a"), tenantsProvider);

        try
        {
            using (accessor.PushContext(Tenant("tenant-a")))
                worker.Start();

            await Task.Delay(50);

            await Assert.That(scopes.OpenedTenantIds).IsEmpty();
            await tenantsProvider.DidNotReceive().ListAsync(Arg.Any<CancellationToken>());
        }
        finally
        {
            worker.Stop();
        }
    }

    [Test]
    public async Task SignalingEmptyQueue_ProcessesOnlyThatTenant()
    {
        var accessor = new DefaultTenantAccessor();
        var signaler = new BookmarkQueueSignaler(accessor);
        var tenantA = Tenant("tenant-a");
        var tenantB = Tenant("tenant-b");
        var tenantsProvider = Substitute.For<ITenantsProvider>();
        var (workerA, processorA, scopesA) = CreateCountingWorker(signaler, accessor, tenantA, tenantsProvider);
        var (workerB, processorB, scopesB) = CreateCountingWorker(signaler, accessor, tenantB, tenantsProvider);

        try
        {
            using (accessor.PushContext(tenantA))
                workerA.Start();
            using (accessor.PushContext(tenantB))
                workerB.Start();

            using (accessor.PushContext(tenantA))
                await signaler.TriggerAsync();

            await WaitUntilAsync(() => processorA.Calls == 1);

            await Assert.That(processorA.AmbientTenantIds).IsEquivalentTo(new string?[] { "tenant-a" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
            await Assert.That(scopesA.OpenedTenantIds).IsEquivalentTo(new string?[] { "tenant-a" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
            await Assert.That(processorB.Calls).IsEqualTo(0);
            await Assert.That(processorB.AmbientTenantIds).IsEmpty();
            await Assert.That(scopesB.OpenedTenantIds).IsEmpty();
            await tenantsProvider.DidNotReceive().ListAsync(Arg.Any<CancellationToken>());
        }
        finally
        {
            workerA.Stop();
            workerB.Stop();
        }
    }

    [Test]
    public async Task SignalFromTenantA_DoesNotWakeTenantB()
    {
        var accessor = new DefaultTenantAccessor();
        var signaler = new BookmarkQueueSignaler(accessor);
        var tenantA = Tenant("tenant-a");
        var tenantB = Tenant("tenant-b");
        var tenantsProvider = Substitute.For<ITenantsProvider>();
        var (workerA, processorA, scopesA) = CreateCountingWorker(signaler, accessor, tenantA, tenantsProvider);
        var (workerB, processorB, _) = CreateCountingWorker(signaler, accessor, tenantB, tenantsProvider);

        try
        {
            using (accessor.PushContext(tenantA))
                workerA.Start();
            using (accessor.PushContext(tenantB))
                workerB.Start();

            using (accessor.PushContext(tenantA))
                await signaler.TriggerAsync();

            await WaitUntilAsync(() => processorA.Calls == 1);

            await Task.Delay(50);
            await Assert.That(processorB.Calls).IsEqualTo(0);
            await Assert.That(processorA.AmbientTenantIds).IsEquivalentTo(new string?[] { "tenant-a" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
            await Assert.That(scopesA.OpenedTenantIds).IsEquivalentTo(new string?[] { "tenant-a" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
            await tenantsProvider.DidNotReceive().ListAsync(Arg.Any<CancellationToken>());
        }
        finally
        {
            workerA.Stop();
            workerB.Stop();
        }
    }

    [Test]
    public async Task SingleTenant_WithoutMultitenancy_StillProcessesQueue()
    {
        var processor = new TenantRecordingProcessor();
        var services = new ServiceCollection()
            .AddSingleton<IBookmarkQueueProcessor>(processor)
            .BuildServiceProvider();
        var signaler = new BookmarkQueueSignaler();
        var worker = new ImmediateBookmarkQueueWorker(
            signaler,
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<BookmarkQueueWorker>.Instance);

        try
        {
            worker.Start();
            await signaler.TriggerAsync();
            await WaitUntilAsync(() => processor.Calls == 1);
        }
        finally
        {
            worker.Stop();
        }
    }

    [Test]
    public async Task SingleTenant_DefaultTenant_WakesWhenSignaledWithoutAmbientTenant()
    {
        var accessor = new DefaultTenantAccessor();
        var signaler = new BookmarkQueueSignaler(accessor);
        var tenantsProvider = Substitute.For<ITenantsProvider>();
        var (worker, processor, _) = CreateCountingWorker(signaler, accessor, Elsa.Common.Multitenancy.Tenant.Default, tenantsProvider);

        try
        {
            using (accessor.PushContext(Elsa.Common.Multitenancy.Tenant.Default))
                worker.Start();

            await Assert.That(accessor.Tenant).IsNull();
            await signaler.TriggerAsync();
            await WaitUntilAsync(() => processor.Calls == 1);

            await Assert.That(processor.AmbientTenantIds).IsEquivalentTo([Elsa.Common.Multitenancy.Tenant.DefaultTenantId], TUnit.Assertions.Enums.CollectionOrdering.Matching);
        }
        finally
        {
            worker.Stop();
        }
    }

    private static (BookmarkQueueWorker Worker, TenantRecordingProcessor Processor) CreateResumingWorker(
        BookmarkQueueSignaler signaler,
        DefaultTenantAccessor accessor,
        Tenant tenant,
        string bookmarkId,
        List<string> resumed,
        ITenantsProvider tenantsProvider)
    {
        var store = new MemoryBookmarkQueueStore(new MemoryStore<BookmarkQueueItem>());
        store.AddAsync(new BookmarkQueueItem
        {
            Id = bookmarkId,
            BookmarkId = bookmarkId,
            WorkflowInstanceId = "instance-" + bookmarkId,
            CreatedAt = DateTimeOffset.UtcNow
        }).GetAwaiter().GetResult();

        var resumer = Substitute.For<IWorkflowResumer>();
        resumer.ResumeAsync(Arg.Any<BookmarkFilter>(), Arg.Any<ResumeBookmarkOptions?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                lock (resumed)
                    resumed.Add(call.Arg<BookmarkFilter>().BookmarkId!);
                return new[]
                {
                    new RunWorkflowInstanceResponse { WorkflowInstanceId = "instance-" + bookmarkId }
                };
            });

        var inner = new BookmarkQueueProcessor(
            store,
            Substitute.For<IBookmarkQueueDeadLetterManager>(),
            resumer,
            Substitute.For<ISystemClock>(),
            Microsoft.Extensions.Options.Options.Create(new BookmarkQueuePurgeOptions()),
            NullLogger<BookmarkQueueProcessor>.Instance);
        var processor = new TenantRecordingProcessor(accessor, inner);

        return (CreateWorker(signaler, accessor, tenant, processor, tenantsProvider).Worker, processor);
    }

    private static (BookmarkQueueWorker Worker, TenantRecordingProcessor Processor, RecordingTenantScopeFactory Scopes) CreateCountingWorker(
        BookmarkQueueSignaler signaler,
        DefaultTenantAccessor accessor,
        Tenant tenant,
        ITenantsProvider tenantsProvider)
    {
        var processor = new TenantRecordingProcessor(accessor);
        var created = CreateWorker(signaler, accessor, tenant, processor, tenantsProvider);
        return (created.Worker, processor, created.Scopes);
    }

    private static (BookmarkQueueWorker Worker, RecordingTenantScopeFactory Scopes) CreateRecordingWorker(
        BookmarkQueueSignaler signaler,
        DefaultTenantAccessor accessor,
        Tenant tenant,
        ITenantsProvider tenantsProvider)
    {
        var created = CreateWorker(signaler, accessor, tenant, new TenantRecordingProcessor(accessor), tenantsProvider);
        return (created.Worker, created.Scopes);
    }

    private static (BookmarkQueueWorker Worker, RecordingTenantScopeFactory Scopes) CreateWorker(
        BookmarkQueueSignaler signaler,
        DefaultTenantAccessor accessor,
        Tenant tenant,
        IBookmarkQueueProcessor processor,
        ITenantsProvider tenantsProvider)
    {
        var services = new ServiceCollection()
            .AddSingleton(processor)
            .AddSingleton(tenantsProvider)
            .BuildServiceProvider();
        var scopeFactory = services.GetRequiredService<IServiceScopeFactory>();
        var scopes = new RecordingTenantScopeFactory(new DefaultTenantScopeFactory(accessor, scopeFactory));

        using (accessor.PushContext(tenant))
        {
            var worker = new ImmediateBookmarkQueueWorker(
                signaler,
                scopeFactory,
                NullLogger<BookmarkQueueWorker>.Instance,
                scopes,
                accessor);
            return (worker, scopes);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException("Condition was not met in time.");
            await Task.Delay(10);
        }
    }

    private static Tenant Tenant(string id) => new() { Id = id, Name = id };

    private sealed class TenantRecordingProcessor(ITenantAccessor? accessor = null, IBookmarkQueueProcessor? inner = null) : IBookmarkQueueProcessor
    {
        public int Calls { get; private set; }
        public List<string> AmbientTenantIds { get; } = [];

        public async Task ProcessAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            AmbientTenantIds.Add((accessor?.TenantId).NormalizeTenantId());
            if (inner is not null)
                await inner.ProcessAsync(cancellationToken);
        }
    }

    private sealed class RecordingTenantScopeFactory(ITenantScopeFactory inner) : ITenantScopeFactory
    {
        public List<string?> OpenedTenantIds { get; } = [];

        public TenantScope CreateScope(Tenant? tenant)
        {
            OpenedTenantIds.Add(tenant?.Id);
            return inner.CreateScope(tenant);
        }
    }
}
