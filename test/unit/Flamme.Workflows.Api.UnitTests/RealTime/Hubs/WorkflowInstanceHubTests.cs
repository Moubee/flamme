using System.Security.Claims;
using Flamme.Common.Multitenancy;
using Flamme.Workflows.Api.RealTime.Hubs;
using Flamme.Workflows.Management;
using Flamme.Workflows.Management.Entities;
using Flamme.Workflows.Management.Filters;
using Microsoft.AspNetCore.SignalR;

namespace Flamme.Workflows.Api.UnitTests.RealTime.Hubs;

public class WorkflowInstanceHubTests
{
    private const string ConnectionId = "connection-1";
    private const string WorkflowInstanceId = "workflow-instance-1";
    private const string TenantId = "tenant-a";
    private const string OtherTenantId = "tenant-b";
    private const string ReadWorkflowInstancesPermission = "workflows/instances:view";
    private const string CustomPermissionsClaimType = "elsa-permissions";
    private readonly IWorkflowInstanceStore _workflowInstanceStore;
    private readonly ITenantAccessor _tenantAccessor;
    private readonly IGroupManager _groups;
    private readonly HubCallerContext _context;
    private readonly WorkflowInstanceHub _hub;
    private readonly CancellationTokenSource _connectionAbortedTokenSource = new();
    private WorkflowInstance? _workflowInstance;

    public WorkflowInstanceHubTests()
    {
        _workflowInstanceStore = IWorkflowInstanceStore.Mock().Object;
        _tenantAccessor = ITenantAccessor.Mock().Object;
        _groups = IGroupManager.Mock().Object;
        _context = HubCallerContext.Mock().Object;
        _workflowInstance = CreateWorkflowInstance(TenantId);
        _hub = new WorkflowInstanceHub(_workflowInstanceStore, _tenantAccessor)
        {
            Context = _context,
            Groups = _groups
        };

        Mock.Get(_tenantAccessor).TenantId.Returns(TenantId);
        Mock.Get(_context).ConnectionId.Returns(ConnectionId);
        Mock.Get(_context).ConnectionAborted.Returns(_connectionAbortedTokenSource.Token);
        UseUser(ReadWorkflowInstancesPermission);
        StubWorkflowInstance();
    }

    [After(Test)]
    public void Cleanup()
    {
        _hub.Dispose();
        _connectionAbortedTokenSource.Dispose();
    }

    [Test]
    public async Task ObserveInstanceAsync_WithReadPermissionAndMatchingTenant_JoinsInstanceGroup()
    {
        await _hub.ObserveInstanceAsync(WorkflowInstanceId);

        Mock.Get(_groups).AddToGroupAsync(ConnectionId, WorkflowInstanceId, _connectionAbortedTokenSource.Token).WasCalled(Times.Exactly(1));
    }

    [Test]
    [Arguments("*")]
    [Arguments("*:view")]
    public async Task ObserveInstanceAsync_WithWildcardReadPermission_JoinsInstanceGroup(string permission)
    {
        UseUser(permission);

        await _hub.ObserveInstanceAsync(WorkflowInstanceId);

        Mock.Get(_groups).AddToGroupAsync(ConnectionId, WorkflowInstanceId, _connectionAbortedTokenSource.Token).WasCalled(Times.Exactly(1));
    }

    [Test]
    public async Task ObserveInstanceAsync_UsesFlammePermissionClaimType()
    {
        // Flamme is the only authority that expands roles into permission claims (ADR 0009), so a
        // similarly named FastEndpoints claim must not grant a Flamme workflow permission.
        UseUserClaim(CustomPermissionsClaimType, ReadWorkflowInstancesPermission);

        await Assert.ThrowsExactlyAsync<HubException>(() => _hub.ObserveInstanceAsync(WorkflowInstanceId));

        Mock.Get(_groups).AddToGroupAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).WasNeverCalled();
    }

    [Test]
    public async Task ObserveInstanceAsync_WithoutReadPermission_DoesNotJoinInstanceGroup()
    {
        UseUser("write:workflow-instances");

        await Assert.ThrowsExactlyAsync<HubException>(() => _hub.ObserveInstanceAsync(WorkflowInstanceId));

        Mock.Get(_groups).AddToGroupAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).WasNeverCalled();
    }

    [Test]
    public async Task ObserveInstanceAsync_WithDifferentTenant_DoesNotJoinInstanceGroup()
    {
        UseWorkflowInstanceTenant(OtherTenantId);

        await Assert.ThrowsExactlyAsync<HubException>(() => _hub.ObserveInstanceAsync(WorkflowInstanceId));

        Mock.Get(_groups).AddToGroupAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).WasNeverCalled();
    }

    [Test]
    public async Task ObserveInstanceAsync_WhenInstanceIsNotVisible_DoesNotJoinInstanceGroup()
    {
        UseWorkflowInstance(null);

        await Assert.ThrowsExactlyAsync<HubException>(() => _hub.ObserveInstanceAsync(WorkflowInstanceId));

        Mock.Get(_groups).AddToGroupAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).WasNeverCalled();
    }

    [Test]
    public async Task ObserveInstanceAsync_WithTenantAgnosticInstance_JoinsInstanceGroup()
    {
        UseWorkflowInstanceTenant(Tenant.AgnosticTenantId);

        await _hub.ObserveInstanceAsync(WorkflowInstanceId);

        Mock.Get(_groups).AddToGroupAsync(ConnectionId, WorkflowInstanceId, _connectionAbortedTokenSource.Token).WasCalled(Times.Exactly(1));
    }

    [Test]
    public async Task ObserveInstanceAsync_WithoutTenantAccessor_JoinsVisibleInstanceGroup()
    {
        using var hub = new WorkflowInstanceHub(_workflowInstanceStore)
        {
            Context = _context,
            Groups = _groups
        };

        await hub.ObserveInstanceAsync(WorkflowInstanceId);

        Mock.Get(_groups).AddToGroupAsync(ConnectionId, WorkflowInstanceId, _connectionAbortedTokenSource.Token).WasCalled(Times.Exactly(1));
    }

    [Test]
    public async Task ObserveInstanceAsync_WithNonCanonicalCurrentTenant_NormalizesBeforeComparing()
    {
        Mock.Get(_tenantAccessor).TenantId.Returns(string.Empty);
        UseWorkflowInstanceTenant(null);

        await _hub.ObserveInstanceAsync(WorkflowInstanceId);

        Mock.Get(_groups).AddToGroupAsync(ConnectionId, WorkflowInstanceId, _connectionAbortedTokenSource.Token).WasCalled(Times.Exactly(1));
    }

    private void UseUser(params string[] permissions)
    {
        var claims = permissions.Select(x => new Claim("permissions", x));
        var identity = new ClaimsIdentity(claims, "Test");
        Mock.Get(_context).User.Returns(new ClaimsPrincipal(identity));
    }

    private void UseUserClaim(string claimType, params string[] permissions)
    {
        var claims = permissions.Select(x => new Claim(claimType, x));
        var identity = new ClaimsIdentity(claims, "Test");
        Mock.Get(_context).User.Returns(new ClaimsPrincipal(identity));
    }

    private void UseWorkflowInstanceTenant(string? tenantId)
    {
        UseWorkflowInstance(CreateWorkflowInstance(tenantId));
    }

    private void UseWorkflowInstance(WorkflowInstance? workflowInstance)
    {
        _workflowInstance = workflowInstance;
        StubWorkflowInstance();
    }

    private void StubWorkflowInstance()
    {
        Mock.Get(_workflowInstanceStore)
            .FindAsync(Arg.Is<WorkflowInstanceFilter>(x => x.Id == WorkflowInstanceId), Arg.Any<CancellationToken>())
            .Returns(() => new ValueTask<WorkflowInstance?>(_workflowInstance));
    }

    private static WorkflowInstance CreateWorkflowInstance(string? tenantId)
    {
        return new()
        {
            Id = WorkflowInstanceId,
            TenantId = tenantId,
            DefinitionId = "definition-1",
            DefinitionVersionId = "definition-version-1"
        };
    }

}
