using Flamme.Common.DistributedHosting;
using Flamme.Common.Services;
using Flamme.Workflows.Runtime.Entities;
using Flamme.Workflows.Runtime.Filters;
using Flamme.Workflows.Runtime.Messages;
using Flamme.Workflows.Runtime.Stores;
using Medallion.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TUnit.Mocks;

namespace Flamme.Workflows.Runtime.UnitTests.Services;

/// <summary>
/// Rolling-upgrade overlap: an old node still locks <c>DistributedBookmarkQueueWorker</c>
/// while a new node locks <c>DistributedBookmarkQueueWorker:{tenantId}</c>, so the same queue
/// item can be processed twice. After the first resume consumes the bookmark, a second resume
/// must be a no-op.
/// </summary>
public class WorkflowResumerConsumedBookmarkTests
{
    [Test]
    public async Task ResumeAsync_WhenBookmarkAlreadyConsumed_IsNoOp()
    {
        var store = new MemoryBookmarkStore(new MemoryStore<StoredBookmark>());
        await store.SaveAsync(new StoredBookmark
        {
            Id = "bookmark-1",
            WorkflowInstanceId = "instance-1",
            Hash = "hash",
            Name = "Signals.Event"
        });

        var runCount = 0;
        var client = IWorkflowClient.Mock().Object;
        Mock.Get(client).RunInstanceAsync(Arg.Any<RunWorkflowInstanceRequest>(), Arg.Any<CancellationToken>())
                    .Returns(() =>
                    {
                        runCount++;
                        return new RunWorkflowInstanceResponse { WorkflowInstanceId = "instance-1" };
                    });

        var runtime = IWorkflowRuntime.Mock().Object;
        Mock.Get(runtime).CreateClientAsync("instance-1", Arg.Any<CancellationToken>()).Returns(client);

        var lockProvider = IDistributedLockProvider.Mock().Object;
        var distributedLock = IDistributedLock.Mock().Object;
        var lockHandle = IDistributedSynchronizationHandle.Mock().Object;
        Mock.Get(lockProvider).CreateLock(Arg.Any<string>()).Returns(distributedLock);
        Mock.Get(distributedLock).AcquireAsync(Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
                    .Returns(lockHandle);

        var resumer = new WorkflowResumer(
            runtime,
            store,
            IStimulusHasher.Mock().Object,
            lockProvider,
            Microsoft.Extensions.Options.Options.Create(new DistributedLockingOptions()),
            NullLogger<WorkflowResumer>.Instance);
        var filter = new BookmarkFilter { BookmarkId = "bookmark-1" };

        var first = (await resumer.ResumeAsync(filter)).ToList();
        await Assert.That(first).HasSingleItem();
        await Assert.That(runCount).IsEqualTo(1);

        await store.DeleteAsync(new BookmarkFilter { BookmarkId = "bookmark-1" });

        var second = (await resumer.ResumeAsync(filter)).ToList();
        await Assert.That(second).IsEmpty();
        await Assert.That(runCount).IsEqualTo(1);
    }
}
