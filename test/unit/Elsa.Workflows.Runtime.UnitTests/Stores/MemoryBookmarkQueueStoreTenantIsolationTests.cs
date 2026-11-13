using Elsa.Common;
using Elsa.Common.Entities;
using Elsa.Common.Models;
using Elsa.Common.Services;
using Elsa.Testing.Shared.Multitenancy;
using Elsa.Workflows.Runtime.Entities;
using Elsa.Workflows.Runtime.Filters;
using Elsa.Workflows.Runtime.Messages;
using Elsa.Workflows.Runtime.Options;
using Elsa.Workflows.Runtime.OrderDefinitions;
using Elsa.Workflows.Runtime.Stores;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Elsa.Workflows.Runtime.UnitTests.Stores;

/// <summary>
/// Memory must honor ambient tenant the same way EF does via <c>SetTenantIdFilter</c>.
/// The unfiltered <c>PageAsync</c> used by <see cref="BookmarkQueueProcessor"/> is the
/// dequeue path Greptile flagged: a tenant-B processor must not see tenant-A items.
/// </summary>
public class MemoryBookmarkQueueStoreTenantIsolationTests
{
    [Test]
    [DisplayName("Item enqueued under tenant A is not returned or resumed by a tenant-B processor")]
    public async Task ProcessAsync_WhenItemEnqueuedByOtherTenant_DoesNotReturnOrResume()
    {
        var memory = new MemoryStore<BookmarkQueueItem>();
        var accessorA = new TestTenantAccessor("tenant-a");
        var accessorB = new TestTenantAccessor("tenant-b");
        var storeA = new MemoryBookmarkQueueStore(memory, accessorA);
        var storeB = new MemoryBookmarkQueueStore(memory, accessorB);

        var identity = Substitute.For<IIdentityGenerator>();
        identity.GenerateId().Returns("queue-a");
        var clock = Substitute.For<ISystemClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);

        var queue = new StoreBookmarkQueue(
            storeA,
            Substitute.For<IBookmarkQueueSignaler>(),
            clock,
            identity,
            NullLogger<StoreBookmarkQueue>.Instance,
            accessorA);

        await queue.EnqueueAsync(new NewBookmarkQueueItem
        {
            WorkflowInstanceId = "instance-a",
            BookmarkId = "bookmark-a",
            StimulusHash = "hash-a",
            ActivityTypeName = "Elsa.HttpEndpoint",
            Options = new ResumeBookmarkOptions()
        });

        var pageB = await storeB.PageAsync(
            PageArgs.FromRange(0, 50),
            new BookmarkQueueItemOrder<DateTimeOffset>(x => x.CreatedAt, OrderDirection.Ascending));

        await Assert.That(pageB.Items).IsEmpty();

        var resumer = Substitute.For<IWorkflowResumer>();
        resumer.ResumeAsync(Arg.Any<BookmarkFilter>(), Arg.Any<ResumeBookmarkOptions?>(), Arg.Any<CancellationToken>())
            .Returns([new RunWorkflowInstanceResponse { WorkflowInstanceId = "instance-b" }]);

        var processor = new BookmarkQueueProcessor(
            storeB,
            Substitute.For<IBookmarkQueueDeadLetterManager>(),
            resumer,
            clock,
            Microsoft.Extensions.Options.Options.Create(new BookmarkQueuePurgeOptions()),
            NullLogger<BookmarkQueueProcessor>.Instance);

        await processor.ProcessAsync();

        await resumer.DidNotReceive()
            .ResumeAsync(Arg.Any<BookmarkFilter>(), Arg.Any<ResumeBookmarkOptions?>(), Arg.Any<CancellationToken>());

        var remaining = (await storeA.FindManyAsync(new BookmarkQueueFilter())).ToList();
        await Assert.That(remaining).HasSingleItem();
        await Assert.That(remaining[0].Id).IsEqualTo("queue-a");
        await Assert.That(remaining[0].TenantId).IsEqualTo("tenant-a");
    }
}
