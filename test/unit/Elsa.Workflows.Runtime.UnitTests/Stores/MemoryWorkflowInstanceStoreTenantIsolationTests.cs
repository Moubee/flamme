using Elsa.Common.Multitenancy;
using Elsa.Common.Services;
using Elsa.Testing.Shared.Multitenancy;
using Elsa.Workflows.Activities;
using Elsa.Workflows.Management.Entities;
using Elsa.Workflows.Management.Filters;
using Elsa.Workflows.Management.Stores;
using Elsa.Workflows.Runtime.ActivationValidators;

namespace Elsa.Workflows.Runtime.UnitTests.Stores;

public class MemoryWorkflowInstanceStoreTenantIsolationTests
{
    [Test]
    public async Task ActivationStrategyCountsOnlyCurrentTenantAndSaveStampsAmbientTenant()
    {
        var backingStore = new MemoryStore<WorkflowInstance>();
        var tenantA = new MemoryWorkflowInstanceStore(backingStore, new TestTenantAccessor("tenant-a"));
        var tenantB = new MemoryWorkflowInstanceStore(backingStore, new TestTenantAccessor("tenant-b"));
        var instance = RunningInstance("instance-a", correlationId: "conversation-1", tenantId: null);
        await tenantA.SaveAsync(instance);

        await Assert.That(instance.TenantId).IsEqualTo("tenant-a");
        await Assert.That(await tenantA.CountAsync(RunningConversation("conversation-1"))).IsEqualTo(1);
        await Assert.That(await tenantB.CountAsync(RunningConversation("conversation-1"))).IsEqualTo(0);

        var workflow = new Workflow();
        var allowedInTenantA = await new CorrelationStrategy(tenantA).GetAllowActivationAsync(new(workflow, "conversation-1", CancellationToken.None));
        var allowedInTenantB = await new CorrelationStrategy(tenantB).GetAllowActivationAsync(new(workflow, "conversation-1", CancellationToken.None));

        await Assert.That(allowedInTenantA).IsFalse();
        await Assert.That(allowedInTenantB).IsTrue();
        await Assert.That(await tenantA.CountAsync(RunningConversation("conversation-1"))).IsEqualTo(1);
        await Assert.That(await tenantB.CountAsync(RunningConversation("conversation-1"))).IsEqualTo(0);
    }

    [Test]
    public async Task TryMarkInterruptedAsyncAppliesTheSameTenantVisibilityAsFind()
    {
        var backingStore = new MemoryStore<WorkflowInstance>();
        var tenantA = new MemoryWorkflowInstanceStore(backingStore, new TestTenantAccessor("tenant-a"));
        var tenantB = new MemoryWorkflowInstanceStore(backingStore, new TestTenantAccessor("tenant-b"));
        await tenantA.SaveAsync(RunningInstance("instance-a", correlationId: "conversation-1", tenantId: null));

        var markedByB = await tenantB.TryMarkInterruptedAsync("instance-a");

        await Assert.That(markedByB).IsFalse();
        await Assert.That(await tenantB.FindAsync(new WorkflowInstanceFilter { Id = "instance-a" })).IsNull();

        var stillOwnedByA = await tenantA.FindAsync(new WorkflowInstanceFilter { Id = "instance-a" });
        await Assert.That(stillOwnedByA).IsNotNull();
        await Assert.That(stillOwnedByA.Status).IsEqualTo(WorkflowStatus.Running);
        await Assert.That(stillOwnedByA.SubStatus).IsNotEqualTo(WorkflowSubStatus.Interrupted);

        var markedByA = await tenantA.TryMarkInterruptedAsync("instance-a");

        await Assert.That(markedByA).IsTrue();
        var interrupted = await tenantA.FindAsync(new WorkflowInstanceFilter { Id = "instance-a" });
        await Assert.That(interrupted).IsNotNull();
        await Assert.That(interrupted.Status).IsEqualTo(WorkflowStatus.Running);
        await Assert.That(interrupted.SubStatus).IsEqualTo(WorkflowSubStatus.Interrupted);
        await Assert.That(interrupted.IsExecuting).IsFalse();
    }

    [Test]
    public async Task AgnosticRowsRemainVisibleWithoutChangingTheirTenant()
    {
        var backingStore = new MemoryStore<WorkflowInstance>();
        var tenantA = new MemoryWorkflowInstanceStore(backingStore, new TestTenantAccessor("tenant-a"));
        var tenantB = new MemoryWorkflowInstanceStore(backingStore, new TestTenantAccessor("tenant-b"));
        var shared = RunningInstance("instance-shared", "shared-correlation", Tenant.AgnosticTenantId);
        await tenantA.SaveAsync(shared);

        await Assert.That(shared.TenantId).IsEqualTo(Tenant.AgnosticTenantId);
        await Assert.That(await tenantA.CountAsync(RunningConversation("shared-correlation"))).IsEqualTo(1);
        await Assert.That(await tenantB.CountAsync(RunningConversation("shared-correlation"))).IsEqualTo(1);
    }

    private static WorkflowInstance RunningInstance(string id, string correlationId, string? tenantId) => new()
    {
        Id = id,
        TenantId = tenantId,
        CorrelationId = correlationId,
        DefinitionId = "conversation-workflow",
        DefinitionVersionId = "conversation-workflow-v1",
        Status = WorkflowStatus.Running
    };

    private static WorkflowInstanceFilter RunningConversation(string correlationId) => new()
    {
        CorrelationId = correlationId,
        WorkflowStatus = WorkflowStatus.Running
    };
}
