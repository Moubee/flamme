using Elsa.Testing.Shared.Databases;
using System.Collections.Concurrent;
using Elsa.Common.Models;
using Elsa.Extensions;
using Elsa.Mediator.Contracts;
using Elsa.Mediator.HostedServices;
using Elsa.Persistence.EFCore.Extensions;
using Elsa.Persistence.EFCore.Modules.Management;
using Elsa.Testing.Shared;
using Elsa.Workflows.Activities;
using Elsa.Workflows.Management;
using Elsa.Workflows.Management.Entities;
using Elsa.Workflows.Management.Filters;
using Elsa.Workflows.Management.Notifications;
using Elsa.Workflows.Models;
using Elsa.Workflows.Runtime;
using Elsa.Workflows.Runtime.ActivationValidators;
using Elsa.Workflows.Runtime.Requests;
using Medallion.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Workflows.IntegrationTests.Scenarios.CorrelatedActivation;

public class Tests
{
    private readonly TextWriter _testOutputHelper;

    public Tests()
    {
        _testOutputHelper = TestContext.Current!.Output.StandardOutput;
    }

    [Test]
    [DisplayName("Concurrent start with the same CorrelationId and CorrelatedSingleton creates one Running instance")]
    public async Task ConcurrentStart_WithCorrelatedSingleton_CreatesOneRunningInstance()
    {
        await using var services = CreateServices();
        await services.PopulateRegistriesAsync();
        var handle = WorkflowDefinitionHandle.ByDefinitionId(nameof(CorrelatedSingletonConversationWorkflow), VersionOptions.Published);
        await using var scope1 = services.CreateAsyncScope();
        await using var scope2 = services.CreateAsyncScope();
        var starter1 = scope1.ServiceProvider.GetRequiredService<IWorkflowStarter>();
        var starter2 = scope2.ServiceProvider.GetRequiredService<IWorkflowStarter>();

        var results = await Task.WhenAll(
            starter1.StartWorkflowAsync(new StartWorkflowRequest
            {
                WorkflowDefinitionHandle = handle,
                CorrelationId = "conversation-1"
            }),
            starter2.StartWorkflowAsync(new StartWorkflowRequest
            {
                WorkflowDefinitionHandle = handle,
                CorrelationId = "conversation-1"
            }));

        await Assert.That(results.Count(result => !result.CannotStart)).IsEqualTo(1);
        await Assert.That(results.Count(result => result.CannotStart)).IsEqualTo(1);
        await Assert.That(await FindRunningAsync(services, nameof(CorrelatedSingletonConversationWorkflow), "conversation-1")).HasSingleItem();
        await Assert.That(await FindByCorrelationAsync(services, nameof(CorrelatedSingletonConversationWorkflow), "conversation-1")).HasSingleItem();
    }

    [Test]
    [DisplayName("Concurrent dispatch with the same CorrelationId and CorrelatedSingleton creates one Running instance")]
    public async Task ConcurrentDispatch_WithCorrelatedSingleton_CreatesOneRunningInstance()
    {
        var savedSignal = new WorkflowInstanceSavedSignal();
        await using var services = CreateServices(serviceCollection =>
        {
            serviceCollection.AddSingleton(savedSignal);
            serviceCollection.AddNotificationHandler<WorkflowInstanceSavedSignal, WorkflowInstanceSaved>(sp => sp.GetRequiredService<WorkflowInstanceSavedSignal>());
        });
        await services.PopulateRegistriesAsync();
        var graph = await FindGraphAsync(services, nameof(CorrelatedSingletonConversationWorkflow));
        var dispatcher = services.GetRequiredService<IWorkflowDispatcher>();
        var commandProcessor = services.GetServices<IHostedService>().OfType<BackgroundCommandSenderHostedService>().Single();
        await commandProcessor.StartAsync(CancellationToken.None);

        try
        {
            await Task.WhenAll(
                dispatcher.DispatchAsync(new DispatchWorkflowDefinitionRequest(graph.Workflow.Identity.Id)
                {
                    CorrelationId = "conversation-1"
                }, null),
                dispatcher.DispatchAsync(new DispatchWorkflowDefinitionRequest(graph.Workflow.Identity.Id)
                {
                    CorrelationId = "conversation-1"
                }, null));

            await savedSignal.Saved.Task.WaitAsync(TimeSpan.FromSeconds(10));

            var instances = await FindRunningAsync(services, nameof(CorrelatedSingletonConversationWorkflow), "conversation-1");
            await Assert.That(instances).HasSingleItem();
            await Assert.That(await FindByCorrelationAsync(services, nameof(CorrelatedSingletonConversationWorkflow), "conversation-1")).HasSingleItem();
        }
        finally
        {
            await commandProcessor.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    [DisplayName("Concurrent dispatch without a restrictive activation strategy allows both instances")]
    [Arguments(nameof(GroupedConversationWorkflow))]
    [Arguments(nameof(AllowAlwaysConversationWorkflow))]
    public async Task ConcurrentDispatch_WithoutRestriction_AllowsMultipleRunningInstances(string definitionId)
    {
        var savedSignal = new WorkflowInstanceSavedSignal(expectedCount: 2);
        await using var services = CreateServices(serviceCollection =>
        {
            serviceCollection.AddSingleton(savedSignal);
            serviceCollection.AddNotificationHandler<WorkflowInstanceSavedSignal, WorkflowInstanceSaved>(sp => sp.GetRequiredService<WorkflowInstanceSavedSignal>());
        });
        await services.PopulateRegistriesAsync();
        var graph = await FindGraphAsync(services, definitionId);
        var dispatcher = services.GetRequiredService<IWorkflowDispatcher>();
        var commandProcessor = services.GetServices<IHostedService>().OfType<BackgroundCommandSenderHostedService>().Single();
        await commandProcessor.StartAsync(CancellationToken.None);

        try
        {
            await Task.WhenAll(
                dispatcher.DispatchAsync(new DispatchWorkflowDefinitionRequest(graph.Workflow.Identity.Id) { CorrelationId = "conversation-1" }, null),
                dispatcher.DispatchAsync(new DispatchWorkflowDefinitionRequest(graph.Workflow.Identity.Id) { CorrelationId = "conversation-1" }, null));

            await savedSignal.Saved.Task.WaitAsync(TimeSpan.FromSeconds(10));

            await Assert.That((await FindRunningAsync(services, definitionId, "conversation-1")).Count).IsEqualTo(2);
            await Assert.That((await FindByCorrelationAsync(services, definitionId, "conversation-1")).Count).IsEqualTo(2);
        }
        finally
        {
            await commandProcessor.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    [DisplayName("Concurrent dispatch on two runtime nodes sharing PostgreSQL and a distributed lock persists one Running instance")]
    public async Task ConcurrentDispatch_AcrossRuntimeNodes_CreatesOneRunningInstance()
    {
        await using var database = new PostgreSqlTestDatabase();
        var connectionString = database.ConnectionString;
        var lockProvider = new BarrierDistributedLockProvider();

        try
        {
            await using var nodeA = CreatePostgreSqlServices(connectionString, lockProvider);
            await using var nodeB = CreatePostgreSqlServices(connectionString, lockProvider);
            await Assert.That(nodeB).IsNotSameReferenceAs(nodeA);
            await Assert.That(nodeA.GetRequiredService<IDistributedLockProvider>()).IsSameReferenceAs(lockProvider);
            await Assert.That(nodeB.GetRequiredService<IDistributedLockProvider>()).IsSameReferenceAs(lockProvider);

            await using (var dbContext = await nodeA.GetRequiredService<IDbContextFactory<ManagementElsaDbContext>>().CreateDbContextAsync())
                await dbContext.Database.EnsureCreatedAsync();

            await Task.WhenAll(nodeA.PopulateRegistriesAsync(), nodeB.PopulateRegistriesAsync());
            var graphA = await FindGraphAsync(nodeA, nameof(CorrelatedSingletonConversationWorkflow));
            var graphB = await FindGraphAsync(nodeB, nameof(CorrelatedSingletonConversationWorkflow));
            var dispatcherA = nodeA.GetRequiredService<IWorkflowDispatcher>();
            var dispatcherB = nodeB.GetRequiredService<IWorkflowDispatcher>();
            var commandProcessorA = nodeA.GetServices<IHostedService>().OfType<BackgroundCommandSenderHostedService>().Single();
            var commandProcessorB = nodeB.GetServices<IHostedService>().OfType<BackgroundCommandSenderHostedService>().Single();
            await Task.WhenAll(
                commandProcessorA.StartAsync(CancellationToken.None),
                commandProcessorB.StartAsync(CancellationToken.None));

            try
            {
                await Task.WhenAll(
                    dispatcherA.DispatchAsync(new DispatchWorkflowDefinitionRequest(graphA.Workflow.Identity.Id) { CorrelationId = "shared-runtime-correlation" }, null),
                    dispatcherB.DispatchAsync(new DispatchWorkflowDefinitionRequest(graphB.Workflow.Identity.Id) { CorrelationId = "shared-runtime-correlation" }, null));

                await lockProvider.TwoActivationLockAttempts.Task.WaitAsync(TimeSpan.FromSeconds(10));
                // The PostgreSQL connection timeout is 30 seconds; allow a transient provider lock wait
                // to complete so the assertion timeout does not fire before the persistence timeout.
                await lockProvider.TwoActivationLockReleases.Task.WaitAsync(TimeSpan.FromSeconds(40));

                await Assert.That(lockProvider.ActivationLockAttempts).IsEqualTo(2);
                await Assert.That(lockProvider.ActivationLockReleases).IsEqualTo(2);
                await Assert.That(lockProvider.ActivationLockNames.Distinct()).HasSingleItem();
                await Assert.That(await FindRunningAsync(nodeA, nameof(CorrelatedSingletonConversationWorkflow), "shared-runtime-correlation")).HasSingleItem();
                await Assert.That(await FindRunningAsync(nodeB, nameof(CorrelatedSingletonConversationWorkflow), "shared-runtime-correlation")).HasSingleItem();
                await Assert.That(await FindByCorrelationAsync(nodeA, nameof(CorrelatedSingletonConversationWorkflow), "shared-runtime-correlation")).HasSingleItem();
                await Assert.That(await FindByCorrelationAsync(nodeB, nameof(CorrelatedSingletonConversationWorkflow), "shared-runtime-correlation")).HasSingleItem();
            }
            finally
            {
                lockProvider.ReleaseActivationBarrier();
                await Task.WhenAll(
                    commandProcessorA.StopAsync(CancellationToken.None),
                    commandProcessorB.StopAsync(CancellationToken.None));
            }
        }
        finally
        {
            lockProvider.ReleaseActivationBarrier();
        }
    }

    [Test]
    [DisplayName("Waiting dispatch resumes its parent when child activation is denied")]
    public async Task DispatchWorkflow_WaitForCompletion_ResumesParentWhenActivationIsDenied()
    {
        var finishedSignal = new WorkflowInstanceFinishedSavedSignal();
        await using var services = CreateServices(serviceCollection =>
        {
            serviceCollection.AddSingleton(finishedSignal);
            serviceCollection.AddNotificationHandler<WorkflowInstanceFinishedSavedSignal, WorkflowInstanceSaved>(sp => sp.GetRequiredService<WorkflowInstanceFinishedSavedSignal>());
        });
        await services.PopulateRegistriesAsync();
        var starter = services.GetRequiredService<IWorkflowStarter>();
        var child = await starter.StartWorkflowAsync(new StartWorkflowRequest
        {
            WorkflowDefinitionHandle = WorkflowDefinitionHandle.ByDefinitionId(nameof(CorrelatedSingletonConversationWorkflow), VersionOptions.Published),
            CorrelationId = "denied-dispatch-correlation"
        });
        await Assert.That(child.CannotStart).IsFalse();

        var commandProcessor = services.GetServices<IHostedService>().OfType<BackgroundCommandSenderHostedService>().Single();
        var parent = await starter.StartWorkflowAsync(new StartWorkflowRequest
        {
            WorkflowDefinitionHandle = WorkflowDefinitionHandle.ByDefinitionId(nameof(WaitForDeniedDispatchConversationWorkflow), VersionOptions.Published)
        });

        await Assert.That(parent.CannotStart).IsFalse();
        var parentInstanceId = (await Assert.That(parent.WorkflowInstanceId).IsTypeOf<string>());
        await commandProcessor.StartAsync(CancellationToken.None);
        try
        {
            await finishedSignal.WaitForFinishAsync(parentInstanceId);

            var persistedParent = (await Assert.That((await FindByDefinitionAsync(services, nameof(WaitForDeniedDispatchConversationWorkflow))).Where(instance => instance.Id == parentInstanceId)).HasSingleItem());
            await Assert.That(persistedParent.Status).IsEqualTo(WorkflowStatus.Finished);
            await Assert.That(persistedParent.WorkflowState.Bookmarks).IsEmpty();

            var persistedChildren = await FindByCorrelationAsync(services, nameof(CorrelatedSingletonConversationWorkflow), "denied-dispatch-correlation");
            await Assert.That((await Assert.That(persistedChildren).HasSingleItem()).Id).IsEqualTo(child.WorkflowInstanceId);
        }
        finally
        {
            await commandProcessor.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    [DisplayName("Waiting bulk dispatch resumes its parent through ChildFaulted when child activation is denied")]
    public async Task BulkDispatchWorkflows_WaitForCompletion_ResumesParentWhenActivationIsDenied()
    {
        var finishedSignal = new WorkflowInstanceFinishedSavedSignal();
        await using var services = CreateServices(serviceCollection =>
        {
            serviceCollection.AddSingleton(finishedSignal);
            serviceCollection.AddNotificationHandler<WorkflowInstanceFinishedSavedSignal, WorkflowInstanceSaved>(sp => sp.GetRequiredService<WorkflowInstanceFinishedSavedSignal>());
        });
        await services.PopulateRegistriesAsync();
        var starter = services.GetRequiredService<IWorkflowStarter>();
        var child = await starter.StartWorkflowAsync(new StartWorkflowRequest
        {
            WorkflowDefinitionHandle = WorkflowDefinitionHandle.ByDefinitionId(nameof(CorrelatedSingletonConversationWorkflow), VersionOptions.Published),
            CorrelationId = "denied-bulk-dispatch-correlation"
        });
        await Assert.That(child.CannotStart).IsFalse();

        var commandProcessor = services.GetServices<IHostedService>().OfType<BackgroundCommandSenderHostedService>().Single();
        var parent = await starter.StartWorkflowAsync(new StartWorkflowRequest
        {
            WorkflowDefinitionHandle = WorkflowDefinitionHandle.ByDefinitionId(nameof(WaitForDeniedBulkDispatchConversationWorkflow), VersionOptions.Published)
        });

        await Assert.That(parent.CannotStart).IsFalse();
        var parentInstanceId = (await Assert.That(parent.WorkflowInstanceId).IsTypeOf<string>());
        var persistedParentBeforeDispatch = (await Assert.That((await FindByDefinitionAsync(services, nameof(WaitForDeniedBulkDispatchConversationWorkflow))).Where(instance => instance.Id == parentInstanceId)).HasSingleItem());
        await Assert.That(persistedParentBeforeDispatch.WorkflowState.Bookmarks).HasSingleItem();

        // Start only the dispatch command processor: no bookmark-queue worker should be needed
        // because denial routing uses the race-safe resume-or-enqueue stimulus sender.
        await commandProcessor.StartAsync(CancellationToken.None);
        try
        {
            await finishedSignal.WaitForFinishAsync(parentInstanceId);

            var persistedParent = (await Assert.That((await FindByDefinitionAsync(services, nameof(WaitForDeniedBulkDispatchConversationWorkflow))).Where(instance => instance.Id == parentInstanceId)).HasSingleItem());
            var activationDenied = await Assert.That(persistedParent.WorkflowState.Output["ActivationDenied"]).IsTypeOf<bool>();

            using (Assert.Multiple())
            {
                await Assert.That(persistedParent.Status).IsEqualTo(WorkflowStatus.Finished);
                await Assert.That(persistedParent.WorkflowState.Bookmarks).IsEmpty();
                await Assert.That(activationDenied).IsTrue();
                await Assert.That(persistedParent.WorkflowState.Properties.ContainsKey(WorkflowInstanceStorageDriver.VariablesDictionaryStateKey)).IsFalse();
            }

            var persistedChildren = await FindByCorrelationAsync(services, nameof(CorrelatedSingletonConversationWorkflow), "denied-bulk-dispatch-correlation");
            await Assert.That((await Assert.That(persistedChildren).HasSingleItem()).Id).IsEqualTo(child.WorkflowInstanceId);
        }
        finally
        {
            await commandProcessor.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    [DisplayName("Without an activation strategy, the same CorrelationId may have many Running instances")]
    public async Task ConcurrentStart_WithoutStrategy_AllowsMultipleRunningInstances()
    {
        await using var services = CreateServices();
        await services.PopulateRegistriesAsync();
        var starter = services.GetRequiredService<IWorkflowStarter>();
        var handle = WorkflowDefinitionHandle.ByDefinitionId(nameof(GroupedConversationWorkflow), VersionOptions.Published);

        var results = await Task.WhenAll(
            starter.StartWorkflowAsync(new StartWorkflowRequest
            {
                WorkflowDefinitionHandle = handle,
                CorrelationId = "conversation-1"
            }),
            starter.StartWorkflowAsync(new StartWorkflowRequest
            {
                WorkflowDefinitionHandle = handle,
                CorrelationId = "conversation-1"
            }));

        foreach (var result in results)
        {
            await Assert.That(result.CannotStart).IsFalse();
        }
        await Assert.That((await FindRunningAsync(services, nameof(GroupedConversationWorkflow), "conversation-1")).Count).IsEqualTo(2);
        await Assert.That((await FindByCorrelationAsync(services, nameof(GroupedConversationWorkflow), "conversation-1")).Count).IsEqualTo(2);
    }

    [Test]
    [DisplayName("Correlation strategies require a non-blank CorrelationId and persist no instance")]
    [Arguments(nameof(CorrelatedSingletonConversationWorkflow))]
    [Arguments(nameof(GlobalCorrelationConversationWorkflow))]
    public async Task Start_WithBlankCorrelationId_ThrowsAndDoesNotPersistInstance(string definitionId)
    {
        await using var services = CreateServices();
        await services.PopulateRegistriesAsync();
        var starter = services.GetRequiredService<IWorkflowStarter>();
        var handle = WorkflowDefinitionHandle.ByDefinitionId(definitionId, VersionOptions.Published);

        var exception = await Assert.ThrowsExactlyAsync<ArgumentException>(() => starter.StartWorkflowAsync(new StartWorkflowRequest
        {
            WorkflowDefinitionHandle = handle
        }));

        await Assert.That(exception.Message).Contains("non-blank correlation ID").WithComparison(StringComparison.OrdinalIgnoreCase);
        await Assert.That(await FindByDefinitionAsync(services, definitionId)).IsEmpty();
    }

    [Test]
    [DisplayName("Without an activation strategy a blank CorrelationId is allowed")]
    public async Task Start_WithoutStrategy_AllowsBlankCorrelationId()
    {
        await using var services = CreateServices();
        await services.PopulateRegistriesAsync();
        var starter = services.GetRequiredService<IWorkflowStarter>();
        var handle = WorkflowDefinitionHandle.ByDefinitionId(nameof(GroupedConversationWorkflow), VersionOptions.Published);

        var result = await starter.StartWorkflowAsync(new StartWorkflowRequest { WorkflowDefinitionHandle = handle });

        await Assert.That(result.CannotStart).IsFalse();
        await Assert.That(await FindByDefinitionAsync(services, nameof(GroupedConversationWorkflow))).HasSingleItem();
    }

    [Test]
    [DisplayName("CorrelatedSingleton scopes uniqueness to DefinitionId + CorrelationId")]
    public async Task CorrelatedSingleton_AllowsSameCorrelationIdOnDifferentDefinitions()
    {
        await using var services = CreateServices();
        await services.PopulateRegistriesAsync();
        var starter = services.GetRequiredService<IWorkflowStarter>();

        var first = await starter.StartWorkflowAsync(new StartWorkflowRequest
        {
            WorkflowDefinitionHandle = WorkflowDefinitionHandle.ByDefinitionId(nameof(CorrelatedSingletonConversationWorkflow), VersionOptions.Published),
            CorrelationId = "shared-conversation"
        });
        var second = await starter.StartWorkflowAsync(new StartWorkflowRequest
        {
            WorkflowDefinitionHandle = WorkflowDefinitionHandle.ByDefinitionId(nameof(OtherCorrelatedSingletonConversationWorkflow), VersionOptions.Published),
            CorrelationId = "shared-conversation"
        });

        await Assert.That(first.CannotStart).IsFalse();
        await Assert.That(second.CannotStart).IsFalse();
        await Assert.That(await FindRunningAsync(services, nameof(CorrelatedSingletonConversationWorkflow), "shared-conversation")).HasSingleItem();
        await Assert.That(await FindRunningAsync(services, nameof(OtherCorrelatedSingletonConversationWorkflow), "shared-conversation")).HasSingleItem();
    }

    [Test]
    [DisplayName("Correlation strategy refuses a second Running instance for the same CorrelationId across definitions")]
    public async Task CorrelationStrategy_RefusesSecondDefinitionWithSameCorrelationId()
    {
        await using var services = CreateServices();
        await services.PopulateRegistriesAsync();
        var starter = services.GetRequiredService<IWorkflowStarter>();

        var first = await starter.StartWorkflowAsync(new StartWorkflowRequest
        {
            WorkflowDefinitionHandle = WorkflowDefinitionHandle.ByDefinitionId(nameof(GlobalCorrelationConversationWorkflow), VersionOptions.Published),
            CorrelationId = "shared-conversation"
        });
        var second = await starter.StartWorkflowAsync(new StartWorkflowRequest
        {
            WorkflowDefinitionHandle = WorkflowDefinitionHandle.ByDefinitionId(nameof(OtherGlobalCorrelationConversationWorkflow), VersionOptions.Published),
            CorrelationId = "shared-conversation"
        });

        await Assert.That(first.CannotStart).IsFalse();
        await Assert.That(second.CannotStart).IsTrue();
        await Assert.That(await FindRunningAsync(services, nameof(GlobalCorrelationConversationWorkflow), "shared-conversation")).HasSingleItem();
        await Assert.That(await FindRunningAsync(services, nameof(OtherGlobalCorrelationConversationWorkflow), "shared-conversation")).IsEmpty();
        await Assert.That(await FindByCorrelationAsync(services, nameof(GlobalCorrelationConversationWorkflow), "shared-conversation")).HasSingleItem();
        await Assert.That(await FindByCorrelationAsync(services, nameof(OtherGlobalCorrelationConversationWorkflow), "shared-conversation")).IsEmpty();
    }

    [Test]
    [DisplayName("Singleton scopes uniqueness to tenant and definition, independent of correlation ID")]
    public async Task Singleton_RefusesSecondRunningInstanceWithDifferentCorrelationId()
    {
        await using var services = CreateServices();
        await services.PopulateRegistriesAsync();
        var starter = services.GetRequiredService<IWorkflowStarter>();
        var handle = WorkflowDefinitionHandle.ByDefinitionId(nameof(SingletonConversationWorkflow), VersionOptions.Published);

        var first = await starter.StartWorkflowAsync(new StartWorkflowRequest { WorkflowDefinitionHandle = handle, CorrelationId = "conversation-1" });
        var second = await starter.StartWorkflowAsync(new StartWorkflowRequest { WorkflowDefinitionHandle = handle, CorrelationId = "conversation-2" });

        await Assert.That(first.CannotStart).IsFalse();
        await Assert.That(second.CannotStart).IsTrue();
        await Assert.That(await FindByDefinitionAsync(services, nameof(SingletonConversationWorkflow))).HasSingleItem();
    }

    [Test]
    [DisplayName("A terminal instance no longer occupies its activation scope")]
    public async Task CorrelatedSingleton_AllowsNewInstanceAfterPriorInstanceFinishes()
    {
        await using var services = CreateServices();
        await services.PopulateRegistriesAsync();
        var starter = services.GetRequiredService<IWorkflowStarter>();
        var handle = WorkflowDefinitionHandle.ByDefinitionId(nameof(CorrelatedSingletonConversationWorkflow), VersionOptions.Published);
        var first = await starter.StartWorkflowAsync(new StartWorkflowRequest { WorkflowDefinitionHandle = handle, CorrelationId = "conversation-terminal" });
        await Assert.That(first.CannotStart).IsFalse();

        var firstInstance = (await Assert.That(await FindByCorrelationAsync(services, nameof(CorrelatedSingletonConversationWorkflow), "conversation-terminal")).HasSingleItem());
        firstInstance.Status = WorkflowStatus.Finished;
        firstInstance.SubStatus = WorkflowSubStatus.Finished;
        firstInstance.WorkflowState.Status = WorkflowStatus.Finished;
        firstInstance.WorkflowState.SubStatus = WorkflowSubStatus.Finished;
        await services.GetRequiredService<IWorkflowInstanceStore>().SaveAsync(firstInstance);

        var second = await starter.StartWorkflowAsync(new StartWorkflowRequest { WorkflowDefinitionHandle = handle, CorrelationId = "conversation-terminal" });

        await Assert.That(second.CannotStart).IsFalse();
        await Assert.That(await FindRunningAsync(services, nameof(CorrelatedSingletonConversationWorkflow), "conversation-terminal")).HasSingleItem();
        await Assert.That((await FindByCorrelationAsync(services, nameof(CorrelatedSingletonConversationWorkflow), "conversation-terminal")).Count).IsEqualTo(2);
    }

    private ServiceProvider CreateServices(Action<IServiceCollection>? configureServices = null)
    {
        var builder = new TestApplicationBuilder(_testOutputHelper);
        if (configureServices != null)
            builder.ConfigureServices(configureServices);

        return (ServiceProvider)builder
            .AddWorkflow<CorrelatedSingletonConversationWorkflow>()
            .AddWorkflow<WaitForDeniedDispatchConversationWorkflow>()
            .AddWorkflow<WaitForDeniedBulkDispatchConversationWorkflow>()
            .AddWorkflow<SingletonConversationWorkflow>()
            .AddWorkflow<GroupedConversationWorkflow>()
            .AddWorkflow<AllowAlwaysConversationWorkflow>()
            .AddWorkflow<OtherCorrelatedSingletonConversationWorkflow>()
            .AddWorkflow<GlobalCorrelationConversationWorkflow>()
            .AddWorkflow<OtherGlobalCorrelationConversationWorkflow>()
            .Build();
    }

    private ServiceProvider CreatePostgreSqlServices(string connectionString, IDistributedLockProvider lockProvider)
    {
        var builder = new TestApplicationBuilder(_testOutputHelper)
            .ConfigureElsa(elsa => elsa
                .UseWorkflowManagement(management => management.UseWorkflowInstances(instances =>
                    instances.UseEntityFrameworkCore(persistence => persistence.UsePostgreSql(connectionString))))
                .UseWorkflowRuntime(runtime => runtime.DistributedLockProvider = _ => lockProvider));

        return (ServiceProvider)builder
            .AddWorkflow<CorrelatedSingletonConversationWorkflow>()
            .Build();
    }

    private static async Task<WorkflowGraph> FindGraphAsync(IServiceProvider services, string definitionId)
    {
        var graph = await services.GetRequiredService<IWorkflowDefinitionService>()
            .FindWorkflowGraphAsync(definitionId, VersionOptions.Published);
        await Assert.That(graph).IsNotNull();
        return graph;
    }

    private static async Task<List<WorkflowInstance>> FindRunningAsync(IServiceProvider services, string definitionId, string? correlationId)
    {
        var filter = new WorkflowInstanceFilter
        {
            DefinitionId = definitionId,
            WorkflowStatus = WorkflowStatus.Running
        };

        if (correlationId != null)
            filter.CorrelationId = correlationId;

        return (await services.GetRequiredService<IWorkflowInstanceStore>().FindManyAsync(filter)).ToList();
    }

    private static async Task<List<WorkflowInstance>> FindByCorrelationAsync(IServiceProvider services, string definitionId, string correlationId) =>
        (await services.GetRequiredService<IWorkflowInstanceStore>().FindManyAsync(new WorkflowInstanceFilter
        {
            DefinitionId = definitionId,
            CorrelationId = correlationId
        })).ToList();

    private static async Task<List<WorkflowInstance>> FindByDefinitionAsync(IServiceProvider services, string definitionId) =>
        (await services.GetRequiredService<IWorkflowInstanceStore>().FindManyAsync(new WorkflowInstanceFilter
        {
            DefinitionId = definitionId
        })).ToList();

    private sealed class WorkflowInstanceSavedSignal(int expectedCount = 1) : INotificationHandler<WorkflowInstanceSaved>
    {
        private int _count;

        public TaskCompletionSource Saved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task HandleAsync(WorkflowInstanceSaved notification, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _count) >= expectedCount)
                Saved.TrySetResult();
            return Task.CompletedTask;
        }
    }

    private sealed class WorkflowInstanceFinishedSavedSignal : INotificationHandler<WorkflowInstanceSaved>
    {
        private readonly ConcurrentDictionary<string, TaskCompletionSource> _pending = new();
        private readonly ConcurrentDictionary<string, byte> _finished = new();

        public Task WaitForFinishAsync(string workflowInstanceId)
        {
            if (_finished.ContainsKey(workflowInstanceId))
                return Task.CompletedTask;

            var completion = _pending.GetOrAdd(workflowInstanceId, _ => new(TaskCreationOptions.RunContinuationsAsynchronously));
            if (_finished.ContainsKey(workflowInstanceId))
                completion.TrySetResult();
            return completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }

        public Task HandleAsync(WorkflowInstanceSaved notification, CancellationToken cancellationToken)
        {
            if (notification.WorkflowInstance.Status != WorkflowStatus.Finished)
                return Task.CompletedTask;

            var workflowInstanceId = notification.WorkflowInstance.Id;
            _finished.TryAdd(workflowInstanceId, 0);
            if (_pending.TryGetValue(workflowInstanceId, out var completion))
                completion.TrySetResult();
            return Task.CompletedTask;
        }
    }

    private sealed class BarrierDistributedLockProvider : IDistributedLockProvider
    {
        private const string ActivationLockPrefix = "workflow-activation:v1:";
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
        private readonly ConcurrentQueue<string> _activationLockNames = new();
        private int _activationLockAttempts;
        private int _activationLockReleases;

        public TaskCompletionSource TwoActivationLockAttempts { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource TwoActivationLockReleases { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int ActivationLockAttempts => Volatile.Read(ref _activationLockAttempts);
        public int ActivationLockReleases => Volatile.Read(ref _activationLockReleases);
        public IReadOnlyCollection<string> ActivationLockNames => _activationLockNames.ToArray();

        public IDistributedLock CreateLock(string name) => new BarrierDistributedLock(this, name, _locks.GetOrAdd(name, _ => new SemaphoreSlim(1, 1)));

        public void ReleaseActivationBarrier() => TwoActivationLockAttempts.TrySetResult();

        private async ValueTask<IDistributedSynchronizationHandle> AcquireAsync(string name, SemaphoreSlim semaphore, TimeSpan? timeout, CancellationToken cancellationToken)
        {
            var isActivationLock = name.StartsWith(ActivationLockPrefix, StringComparison.Ordinal);
            if (isActivationLock)
            {
                _activationLockNames.Enqueue(name);
                if (Interlocked.Increment(ref _activationLockAttempts) == 2)
                    TwoActivationLockAttempts.TrySetResult();
                await TwoActivationLockAttempts.Task.WaitAsync(cancellationToken);
            }

            if (!await semaphore.WaitAsync(timeout ?? Timeout.InfiniteTimeSpan, cancellationToken))
                throw new TimeoutException($"Could not acquire lock '{name}'.");

            return new Handle(this, name, semaphore, isActivationLock);
        }

        private void Release(string name, SemaphoreSlim semaphore, bool isActivationLock)
        {
            semaphore.Release();
            if (isActivationLock && Interlocked.Increment(ref _activationLockReleases) == 2)
                TwoActivationLockReleases.TrySetResult();
        }

        private sealed class BarrierDistributedLock(BarrierDistributedLockProvider provider, string name, SemaphoreSlim semaphore) : IDistributedLock
        {
            public string Name => name;

            public IDistributedSynchronizationHandle? TryAcquire(TimeSpan timeout = default, CancellationToken cancellationToken = default) =>
                semaphore.Wait(timeout, cancellationToken) ? new Handle(provider, name, semaphore, name.StartsWith(ActivationLockPrefix, StringComparison.Ordinal)) : null;

            public IDistributedSynchronizationHandle Acquire(TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
                provider.AcquireAsync(name, semaphore, timeout, cancellationToken).AsTask().GetAwaiter().GetResult();

            public ValueTask<IDistributedSynchronizationHandle?> TryAcquireAsync(TimeSpan timeout = default, CancellationToken cancellationToken = default) =>
                TryAcquireCoreAsync(timeout, cancellationToken);

            private async ValueTask<IDistributedSynchronizationHandle?> TryAcquireCoreAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
                await semaphore.WaitAsync(timeout, cancellationToken)
                    ? new Handle(provider, name, semaphore, name.StartsWith(ActivationLockPrefix, StringComparison.Ordinal))
                    : null;

            public ValueTask<IDistributedSynchronizationHandle> AcquireAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
                provider.AcquireAsync(name, semaphore, timeout, cancellationToken);
        }

        private sealed class Handle(BarrierDistributedLockProvider provider, string name, SemaphoreSlim semaphore, bool isActivationLock) : IDistributedSynchronizationHandle
        {
            private int _disposed;

            public CancellationToken HandleLostToken => CancellationToken.None;

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0)
                    provider.Release(name, semaphore, isActivationLock);
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
