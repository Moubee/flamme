using Elsa.Common.Multitenancy;
using Elsa.Workflows.Runtime.Entities;
using Elsa.Workflows.Runtime.Handlers;
using Elsa.Workflows.Runtime.Notifications;
using Elsa.Workflows.Runtime.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elsa.Workflows.Runtime.UnitTests.Services;

/// <summary>
/// Verifies that the ambient tenant is readable on the bookmark-queue signal and worker Start paths.
/// Sequential notification publish (used by <c>DefaultCommitStateHandler.FlushAsync</c>) runs handlers
/// on the same call stack as the publisher, so AsyncLocal tenant flows into <see cref="SignalBookmarkQueueWorker"/>.
/// </summary>
public class BookmarkQueueAmbientTenantTests
{
    [Test]
    public async Task SignalBookmarkQueueWorker_CanReadAmbientTenant_WhenSaveNotificationIsHandled()
    {
        var accessor = new DefaultTenantAccessor();
        var signaler = new BookmarkQueueSignaler(accessor);
        var handler = new SignalBookmarkQueueWorker(signaler);
        var tenant = Tenant("tenant-a");
        var awaiter = AwaitTenantAsync(signaler, accessor, "tenant-a");

        using (accessor.PushContext(tenant))
            await handler.HandleAsync(new BookmarkSaved(Bookmark()), CancellationToken.None);

        await Assert.That(await awaiter).IsEqualTo("tenant-a");
    }

    [Test]
    public async Task BookmarkQueueWorker_CanReadAmbientTenant_WhenStarted()
    {
        var accessor = new DefaultTenantAccessor();
        var tenant = Tenant("tenant-a");
        var services = new ServiceCollection().AddSingleton<IBookmarkQueueProcessor>(new RecordingBookmarkQueueProcessor()).BuildServiceProvider();
        var worker = new ImmediateBookmarkQueueWorker(
            new BookmarkQueueSignaler(accessor),
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<BookmarkQueueWorker>.Instance,
            new DefaultTenantScopeFactory(accessor, services.GetRequiredService<IServiceScopeFactory>()),
            accessor);

        using (accessor.PushContext(tenant))
            worker.Start();

        try
        {
            await Assert.That(worker.CapturedTenantId).IsEqualTo("tenant-a");
        }
        finally
        {
            worker.Stop();
        }
    }

    [Test]
    public async Task RecurringTask_ExecuteAsync_SignalsCapturedTenant_WhenAmbientTenantIsMissing()
    {
        var accessor = new DefaultTenantAccessor();
        var signaler = new BookmarkQueueSignaler(accessor);
        var processor = new RecordingBookmarkQueueProcessor(accessor);
        using var services = new ServiceCollection().AddSingleton<IBookmarkQueueProcessor>(processor).BuildServiceProvider();
        var worker = new ImmediateBookmarkQueueWorker(
            signaler,
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<BookmarkQueueWorker>.Instance,
            new DefaultTenantScopeFactory(accessor, services.GetRequiredService<IServiceScopeFactory>()),
            accessor);
        var task = new TriggerBookmarkQueueRecurringTask(worker, signaler, accessor);
        var tenant = Tenant("tenant-a");

        try
        {
            using (accessor.PushContext(tenant))
                await task.StartAsync(CancellationToken.None);

            await Assert.That(accessor.Tenant).IsNull();
            await task.ExecuteAsync(CancellationToken.None);

            // The worker is the channel's only reader. Observe its actual processing tenant rather
            // than racing it with a second reader for the recurring task's single signal.
            await Assert.That(await processor.ProcessedTenant.WaitAsync(TimeSpan.FromSeconds(5))).IsEqualTo("tenant-a");
        }
        finally
        {
            await task.StopAsync(CancellationToken.None);
        }
    }

    private static Task<string> AwaitTenantAsync(IBookmarkQueueSignaler signaler, ITenantAccessor accessor, string tenantId)
    {
        return Task.Run(async () =>
        {
            using (accessor.PushContext(Tenant(tenantId)))
            {
                await signaler.AwaitAsync();
                return accessor.TenantId;
            }
        });
    }

    private static Tenant Tenant(string id) => new() { Id = id, Name = id };

    private static StoredBookmark Bookmark() => new()
    {
        Id = "bookmark-1",
        WorkflowInstanceId = "instance-1",
        Hash = "hash"
    };

    private sealed class RecordingBookmarkQueueProcessor(ITenantAccessor? tenantAccessor = null) : IBookmarkQueueProcessor
    {
        private readonly TaskCompletionSource<string?> _processedTenant = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<string?> ProcessedTenant => _processedTenant.Task;

        public Task ProcessAsync(CancellationToken cancellationToken = default)
        {
            _processedTenant.TrySetResult(tenantAccessor?.TenantId);
            return Task.CompletedTask;
        }
    }
}
