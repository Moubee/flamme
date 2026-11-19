using Flamme.Common.Services;
using Flamme.Workflows.Management.Entities;
using Flamme.Workflows.Management.Filters;
using Flamme.Workflows.Management.Stores;

namespace Flamme.Workflows.Management.UnitTests.Stores;

public class MemoryWorkflowInstanceStoreTests
{
    [Test]
    [DisplayName("TryMarkInterruptedAsync applies Interrupted only when the instance is still Running")]
    public async Task TryMarkInterrupted_MarksRunningInstance()
    {
        var store = CreateStore(new WorkflowInstance
        {
            Id = "running-1",
            DefinitionId = "def-1",
            DefinitionVersionId = "ver-1",
            Version = 1,
            Status = WorkflowStatus.Running,
            SubStatus = WorkflowSubStatus.Executing,
            IsExecuting = true,
        });

        var marked = await store.TryMarkInterruptedAsync("running-1");

        await Assert.That(marked).IsTrue();
        var instance = await store.FindAsync(new() { Id = "running-1" });
        await Assert.That(instance).IsNotNull();
        await Assert.That(instance.Status).IsEqualTo(WorkflowStatus.Running);
        await Assert.That(instance.SubStatus).IsEqualTo(WorkflowSubStatus.Interrupted);
        await Assert.That(instance.IsExecuting).IsFalse();
    }

    [Test]
    [DisplayName("TryMarkInterruptedAsync refuses Finished/Cancelled even when allowFinishedCancelled is true")]
    public async Task TryMarkInterrupted_DoesNotOverwriteCancelledInstance()
    {
        var store = CreateStore(new WorkflowInstance
        {
            Id = "cancelled-1",
            DefinitionId = "def-1",
            DefinitionVersionId = "ver-1",
            Version = 1,
            Status = WorkflowStatus.Finished,
            SubStatus = WorkflowSubStatus.Cancelled,
            IsExecuting = false,
        });

        var markedByDefault = await store.TryMarkInterruptedAsync("cancelled-1");
        var markedWhenAllowed = await store.TryMarkInterruptedAsync("cancelled-1", allowFinishedCancelled: true);

        await Assert.That(markedByDefault).IsFalse();
        await Assert.That(markedWhenAllowed).IsFalse();
        var instance = await store.FindAsync(new() { Id = "cancelled-1" });
        await Assert.That(instance).IsNotNull();
        await Assert.That(instance.Status).IsEqualTo(WorkflowStatus.Finished);
        await Assert.That(instance.SubStatus).IsEqualTo(WorkflowSubStatus.Cancelled);
    }

    [Test]
    [DisplayName("TryMarkInterruptedAsync still refuses Finished/Finished when allowFinishedCancelled is true")]
    public async Task TryMarkInterrupted_DoesNotOverwriteFinishedEvenWhenCancelledAllowed()
    {
        var store = CreateStore(new WorkflowInstance
        {
            Id = "finished-1",
            DefinitionId = "def-1",
            DefinitionVersionId = "ver-1",
            Version = 1,
            Status = WorkflowStatus.Finished,
            SubStatus = WorkflowSubStatus.Finished,
            IsExecuting = false,
        });

        var marked = await store.TryMarkInterruptedAsync("finished-1", allowFinishedCancelled: true);

        await Assert.That(marked).IsFalse();
        var instance = await store.FindAsync(new() { Id = "finished-1" });
        await Assert.That(instance).IsNotNull();
        await Assert.That(instance.Status).IsEqualTo(WorkflowStatus.Finished);
        await Assert.That(instance.SubStatus).IsEqualTo(WorkflowSubStatus.Finished);
    }

    [Test]
    [DisplayName("TryMarkInterruptedAsync does not overwrite a Finished instance")]
    public async Task TryMarkInterrupted_DoesNotOverwriteFinishedInstance()
    {
        var store = CreateStore(new WorkflowInstance
        {
            Id = "finished-1",
            DefinitionId = "def-1",
            DefinitionVersionId = "ver-1",
            Version = 1,
            Status = WorkflowStatus.Finished,
            SubStatus = WorkflowSubStatus.Finished,
            IsExecuting = false,
        });

        var marked = await store.TryMarkInterruptedAsync("finished-1");

        await Assert.That(marked).IsFalse();
        var instance = await store.FindAsync(new() { Id = "finished-1" });
        await Assert.That(instance).IsNotNull();
        await Assert.That(instance.Status).IsEqualTo(WorkflowStatus.Finished);
        await Assert.That(instance.SubStatus).IsEqualTo(WorkflowSubStatus.Finished);
    }

    [Test]
    [DisplayName("TryMarkInterruptedAsync does not replace a concurrent Finished commit with a stale Running snapshot")]
    public async Task TryMarkInterrupted_DoesNotClobberConcurrentFinishedCommit()
    {
        var memory = new MemoryStore<WorkflowInstance>();
        var store = new MemoryWorkflowInstanceStore(memory);
        await store.SaveAsync(new WorkflowInstance
        {
            Id = "raced-1",
            DefinitionId = "def-1",
            DefinitionVersionId = "ver-1",
            Version = 1,
            Status = WorkflowStatus.Running,
            SubStatus = WorkflowSubStatus.Executing,
            IsExecuting = true,
        });

        var staleRunning = await store.FindAsync(new() { Id = "raced-1" });
        await Assert.That(staleRunning).IsNotNull();

        await store.SaveAsync(new WorkflowInstance
        {
            Id = "raced-1",
            DefinitionId = "def-1",
            DefinitionVersionId = "ver-1",
            Version = 1,
            Status = WorkflowStatus.Finished,
            SubStatus = WorkflowSubStatus.Finished,
            IsExecuting = false,
        });

        staleRunning.SubStatus = WorkflowSubStatus.Interrupted;
        staleRunning.IsExecuting = false;

        var marked = await store.TryMarkInterruptedAsync("raced-1");

        await Assert.That(marked).IsFalse();
        var instance = await store.FindAsync(new() { Id = "raced-1" });
        await Assert.That(instance).IsNotNull();
        await Assert.That(instance.Status).IsEqualTo(WorkflowStatus.Finished);
        await Assert.That(instance.SubStatus).IsEqualTo(WorkflowSubStatus.Finished);
        await Assert.That(instance).IsNotSameReferenceAs(staleRunning);
    }

    [Test]
    [DisplayName("TryMarkInterruptedAsync does not leave Finished+Interrupted when SaveAsync completes concurrently")]
    public async Task TryMarkInterrupted_DoesNotLeaveFinishedInterruptedWhenSaveCompletes()
    {
        for (var i = 0; i < 200; i++)
        {
            var store = new MemoryWorkflowInstanceStore(new MemoryStore<WorkflowInstance>());
            await store.SaveAsync(new WorkflowInstance
            {
                Id = "save-race-1",
                DefinitionId = "def-1",
                DefinitionVersionId = "ver-1",
                Version = 1,
                Status = WorkflowStatus.Running,
                SubStatus = WorkflowSubStatus.Executing,
                IsExecuting = true,
            });

            var mark = Task.Run(() => store.TryMarkInterruptedAsync("save-race-1").AsTask());
            var complete = Task.Run(() => store.SaveAsync(new WorkflowInstance
            {
                Id = "save-race-1",
                DefinitionId = "def-1",
                DefinitionVersionId = "ver-1",
                Version = 1,
                Status = WorkflowStatus.Finished,
                SubStatus = WorkflowSubStatus.Finished,
                IsExecuting = false,
            }).AsTask());

            await Task.WhenAll(mark, complete);

            var stored = await store.FindAsync(new WorkflowInstanceFilter { Id = "save-race-1" });
            await Assert.That(stored).IsNotNull();
            await Assert.That(stored.Status == WorkflowStatus.Finished && stored.SubStatus == WorkflowSubStatus.Interrupted).IsFalse().Because($"Finished+Interrupted after SaveAsync completion race (iteration {i}).");
        }
    }

    private static MemoryWorkflowInstanceStore CreateStore(WorkflowInstance instance)
    {
        var store = new MemoryWorkflowInstanceStore(new MemoryStore<WorkflowInstance>());
        store.SaveAsync(instance).AsTask().GetAwaiter().GetResult();
        return store;
    }
}
