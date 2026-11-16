using Elsa.Common.DistributedHosting;
using Elsa.Resilience;
using Elsa.Workflows.Activities;
using Elsa.Workflows.Management;
using Elsa.Workflows.Management.Entities;
using Elsa.Workflows.Management.Mappers;
using Elsa.Workflows.Management.Models;
using Elsa.Workflows.Management.Options;
using Elsa.Workflows.Models;
using Elsa.Workflows.Options;
using Elsa.Workflows.Runtime.Distributed;
using Elsa.Workflows.Runtime.Messages;
using Elsa.Workflows.State;
using Medallion.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TUnit.Mocks;

namespace Elsa.Workflows.Runtime.UnitTests.Services;

public class DistributedWorkflowClientTests
{
    [Test]
    public async Task CreateAndRunInstanceAsync_PreservesSchedulingMetadata()
    {
        var instanceManager = IWorkflowInstanceManager.Mock().Object;
        var definitionService = IWorkflowDefinitionService.Mock().Object;
        var runner = IWorkflowRunner.Mock().Object;
        var activationGate = IWorkflowActivationGate.Mock().Object;
        Mock.Get(activationGate).EvaluateAsync(Arg.Any<Workflow>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
                    .Returns(new WorkflowActivationLease(true, null));

        var workflow = new Workflow { Id = "definition-1", Identity = new WorkflowIdentity("definition-1", 1, "version-1") };
        var node = new ActivityNode(workflow, "root");
        var graph = new WorkflowGraph(workflow, node, [node]);
        var definition = new WorkflowDefinition { Id = "version-1", DefinitionId = "definition-1" };
        Mock.Get(definitionService).TryFindWorkflowGraphAsync(Arg.Any<WorkflowDefinitionHandle>(), Arg.Any<CancellationToken>())
                    .Returns(new WorkflowGraphFindResult(definition, graph));

        var state = new WorkflowState
        {
            Id = "instance-1",
            DefinitionId = "definition-1",
            DefinitionVersionId = "version-1",
            Status = WorkflowStatus.Running,
            SubStatus = WorkflowSubStatus.Pending
        };
        var instance = new WorkflowInstance
        {
            Id = "instance-1",
            DefinitionId = "definition-1",
            DefinitionVersionId = "version-1",
            Status = WorkflowStatus.Running,
            WorkflowState = state
        };
        Mock.Get(instanceManager).CreateWorkflowInstance(Arg.Any<Workflow>(), Arg.Any<WorkflowInstanceOptions?>()).Returns(instance);

        RunWorkflowOptions? observedOptions = null;
        Mock.Get(runner).RunAsync(Arg.Any<WorkflowGraph>(), Arg.Any<WorkflowState>(), Arg.Any<RunWorkflowOptions?>(), Arg.Any<CancellationToken>())
                    .Returns((global::Elsa.Workflows.Models.WorkflowGraph __arg0, global::Elsa.Workflows.State.WorkflowState __arg1, global::Elsa.Workflows.Options.RunWorkflowOptions? __arg2, global::System.Threading.CancellationToken __arg3) =>
                    {
                        observedOptions = __arg2;
                        var resultState = new WorkflowState
                        {
                            Id = "instance-1",
                            DefinitionId = "definition-1",
                            DefinitionVersionId = "version-1",
                            Status = WorkflowStatus.Running,
                            SubStatus = WorkflowSubStatus.Suspended
                        };
                        return Task.FromResult(new RunWorkflowResult(null!, resultState, workflow, null, Journal.Empty));
                    });

        var lockProvider = IDistributedLockProvider.Mock().Object;
        var distributedLock = IDistributedLock.Mock().Object;
        var lockHandle = IDistributedSynchronizationHandle.Mock().Object;
        Mock.Get(lockProvider).CreateLock(Arg.Any<string>()).Returns(distributedLock);
        Mock.Get(distributedLock).AcquireAsync(Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
                    .Returns(lockHandle);

        var services = new ServiceCollection()
            .AddSingleton(instanceManager)
            .AddSingleton(definitionService)
            .AddSingleton(runner)
            .AddSingleton(IWorkflowCanceler.Mock().Object)
            .AddSingleton(activationGate)
            .AddSingleton(WorkflowStateMapper.Mock().Object)
            .AddSingleton(ILogger<LocalWorkflowClient>.Mock().Object)
            .BuildServiceProvider();

        var client = new DistributedWorkflowClient(
            "instance-1",
            lockProvider,
            ITransientExceptionDetector.Mock().Object,
            Microsoft.Extensions.Options.Options.Create(new DistributedLockingOptions()),
            services,
            ILogger<DistributedWorkflowClient>.Mock().Object);

        var response = await client.CreateAndRunInstanceAsync(new CreateAndRunWorkflowInstanceRequest
        {
            WorkflowDefinitionHandle = WorkflowDefinitionHandle.ByDefinitionVersionId("version-1"),
            SchedulingActivityExecutionId = "scheduled-execution-17",
            SchedulingWorkflowInstanceId = "parent-instance-8",
            SchedulingCallStackDepth = 6
        });

        await Assert.That(response.CannotStart).IsFalse();
        await Assert.That(observedOptions).IsNotNull();
        await Assert.That(observedOptions.SchedulingActivityExecutionId).IsEqualTo("scheduled-execution-17");
        await Assert.That(observedOptions.SchedulingWorkflowInstanceId).IsEqualTo("parent-instance-8");
        await Assert.That(observedOptions.SchedulingCallStackDepth).IsEqualTo(6);
    }
}
