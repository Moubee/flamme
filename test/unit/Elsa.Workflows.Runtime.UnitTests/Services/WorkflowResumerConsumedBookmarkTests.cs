using Elsa.Common.DistributedHosting;
using Elsa.Common.Services;
using Elsa.Workflows.Runtime.Entities;
using Elsa.Workflows.Runtime.Filters;
using Elsa.Workflows.Runtime.Messages;
using Elsa.Workflows.Runtime.Stores;
using Medallion.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TUnit.Mocks;

namespace Elsa.Workflows.Runtime.UnitTests.Services;

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
            Name = "Elsa.Event"
        });

        var runCount = 0;
        var client = Mock.Of<IWorkflowClient>().Object;
        Mock.Get(client).RunInstanceAsync(Arg.Any<RunWorkflowInstanceRequest>(), Arg.Any<CancellationToken>())
                    .Returns(() =>
                    {
                        runCount++;
                        return new RunWorkflowInstanceResponse { WorkflowInstanceId = "instance-1" };
                    });

        var runtime = Mock.Of<IWorkflowRuntime>().Object;
        Mock.Get(runtime).CreateClientAsync("instance-1", Arg.Any<CancellationToken>()).Returns(client);

        var lockProvider = Mock.Of<IDistributedLockProvider>().Object;
        var distributedLock = Mock.Of<IDistributedLock>().Object;
        var lockHandle = Mock.Of<IDistributedSynchronizationHandle>().Object;
        Mock.Get(lockProvider).CreateLock(Arg.Any<string>()).Returns(distributedLock);
        Mock.Get(distributedLock).AcquireAsync(Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
                    .Returns(lockHandle);

        var resumer = new WorkflowResumer(
            runtime,
            store,
            Mock.Of<IStimulusHasher>().Object,
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
