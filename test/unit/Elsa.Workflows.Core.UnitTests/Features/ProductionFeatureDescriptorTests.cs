using System.ComponentModel;
using System.Diagnostics;
using Elsa.Common;
using Elsa.Common.Features;
using Elsa.Common.Multitenancy;
using Elsa.Common.Multitenancy.EventHandlers;
using Elsa.Common.Multitenancy.HostedServices;
using Elsa.Common.RecurringTasks;
using Elsa.Common.Serialization;
using Elsa.Common.Services;
using Elsa.Expressions.Contracts;
using Elsa.Expressions.Features;
using Elsa.Expressions.Services;
using Elsa.Features.Implementations;
using Elsa.Features.Services;
using Elsa.Mediator.Channels;
using Elsa.Mediator.Contracts;
using Elsa.Mediator.HostedServices;
using Elsa.Mediator.Middleware.Command;
using Elsa.Mediator.Middleware.Command.Contracts;
using Elsa.Mediator.Middleware.Notification;
using Elsa.Mediator.Middleware.Notification.Contracts;
using Elsa.Mediator.Middleware.Request;
using Elsa.Mediator.Middleware.Request.Contracts;
using Elsa.Mediator.Services;
using Elsa.Workflows.Builders;
using Elsa.Workflows.CommitStates;
using Elsa.Workflows.CommitStates.Tasks;
using Elsa.Workflows.Features;
using Elsa.Workflows.IncidentStrategies;
using Elsa.Workflows.Pipelines.ActivityExecution;
using Elsa.Workflows.Pipelines.WorkflowExecution;
using Elsa.Workflows.Serialization.Serializers;
using Elsa.Workflows.Services;
using Medallion.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TUnit.Mocks;

namespace Elsa.Workflows.Core.UnitTests.Features;

/// <summary>
/// Characterizes selected production registration boundaries before the feature
/// metadata provider changes. Framework-owned options/logging descriptors are
/// deliberately outside these fixtures.
/// </summary>
public class ProductionFeatureDescriptorTests
{
    private const string IsolatedFormatterMarker = "ELSA_FEATURE_FORMATTER_ISOLATED_TEST";

    [Test]
    public async Task TimeProvider_PreservesHostInstanceAndSingletonDescriptor()
    {
        var services = new ServiceCollection();
        var module = new Module(services);
        new TimeProviderFeature(module).Apply();

        await AssertBindingsAsync(services, [Instance<TimeProvider>(ServiceLifetime.Singleton)]);
        await Assert.That(services.Single().ImplementationInstance).IsSameReferenceAs(TimeProvider.System);

        var hostProvider = new HostTimeProvider();
        services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(hostProvider);
        new TimeProviderFeature(new Module(services)).Apply();
        await Assert.That(services.Single().ImplementationInstance).IsSameReferenceAs(hostProvider);
    }

    [Test]
    public async Task Expressions_PreservesEvaluatorAndRegistryOrderAndLifetimes()
    {
        var services = ApplyFeature(module => new ExpressionsFeature(module));
        await AssertBindingsAsync(services,
        [
            TypeBinding<IExpressionEvaluator, ExpressionEvaluator>(ServiceLifetime.Scoped),
            TypeBinding<IWellKnownTypeRegistry, WellKnownTypeRegistry>(ServiceLifetime.Singleton)
        ]);
    }

    [Test]
    public async Task Mediator_PreservesDispatchAliasesPipelinesAndBackgroundServices()
    {
        var services = ApplyFeature(module => new MediatorFeature(module));
        await AssertBindingsAsync(services,
        [
            TypeBinding<IMediator, DefaultMediator>(ServiceLifetime.Scoped),
            Factory<IRequestSender>(ServiceLifetime.Scoped),
            Factory<ICommandSender>(ServiceLifetime.Scoped),
            Factory<INotificationSender>(ServiceLifetime.Scoped),
            TypeBinding<IRequestPipeline, RequestPipeline>(ServiceLifetime.Singleton),
            TypeBinding<ICommandPipeline, CommandPipeline>(ServiceLifetime.Singleton),
            TypeBinding<INotificationPipeline, NotificationPipeline>(ServiceLifetime.Singleton),
            TypeBinding<INotificationsChannel, NotificationsChannel>(ServiceLifetime.Singleton),
            TypeBinding<ICommandsChannel, CommandsChannel>(ServiceLifetime.Singleton),
            TypeBinding<IJobsChannel, JobsChannel>(ServiceLifetime.Singleton),
            TypeBinding<IJobQueue, JobQueue>(ServiceLifetime.Singleton),
            TypeBinding<BackgroundCommandProcessor, BackgroundCommandProcessor>(ServiceLifetime.Singleton),
            TypeBinding<BackgroundNotificationProcessor, BackgroundNotificationProcessor>(ServiceLifetime.Singleton),
            TypeBinding<BackgroundJobProcessor, BackgroundJobProcessor>(ServiceLifetime.Singleton),
            TypeBinding<IHostedService, JobRunnerHostedService>(ServiceLifetime.Singleton),
            TypeBinding<IHostedService, BackgroundCommandSenderHostedService>(ServiceLifetime.Singleton),
            TypeBinding<IHostedService, BackgroundEventPublisherHostedService>(ServiceLifetime.Singleton)
        ]);

        services.AddLogging();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var firstScope = provider.CreateAsyncScope();
        await using var secondScope = provider.CreateAsyncScope();
        var mediator = firstScope.ServiceProvider.GetRequiredService<IMediator>();
        await Assert.That(firstScope.ServiceProvider.GetRequiredService<IRequestSender>()).IsSameReferenceAs(mediator);
        await Assert.That(firstScope.ServiceProvider.GetRequiredService<ICommandSender>()).IsSameReferenceAs(mediator);
        await Assert.That(firstScope.ServiceProvider.GetRequiredService<INotificationSender>()).IsSameReferenceAs(mediator);
        await Assert.That(ReferenceEquals(mediator, secondScope.ServiceProvider.GetRequiredService<IMediator>())).IsFalse();
        await Assert.That(firstScope.ServiceProvider.GetRequiredService<INotificationPipeline>())
            .IsSameReferenceAs(secondScope.ServiceProvider.GetRequiredService<INotificationPipeline>());
    }

    [Test]
    public async Task Multitenancy_PreservesActivationPlacementAndTaskDescriptors()
    {
        var services = ApplyFeature(module => new MultitenancyFeature(module));
        await AssertBindingsAsync(services,
        [
            TypeBinding<IHostedService, ActivateTenants>(ServiceLifetime.Singleton),
            TypeBinding<ITenantScopeFactory, DefaultTenantScopeFactory>(ServiceLifetime.Singleton),
            TypeBinding<ITenantAccessor, DefaultTenantAccessor>(ServiceLifetime.Singleton),
            TypeBinding<ITenantFinder, DefaultTenantFinder>(ServiceLifetime.Singleton),
            TypeBinding<ITenantService, DefaultTenantService>(ServiceLifetime.Singleton),
            TypeBinding<TenantTaskLifecycleCoordinator, TenantTaskLifecycleCoordinator>(ServiceLifetime.Singleton),
            TypeBinding<TenantTaskLifecycleEventHandler, TenantTaskLifecycleEventHandler>(ServiceLifetime.Singleton),
            Factory<ITenantActivatedEvent>(ServiceLifetime.Singleton),
            Factory<ITenantDeactivatedEvent>(ServiceLifetime.Singleton),
            TypeBinding<RecurringTaskScheduleManager, RecurringTaskScheduleManager>(ServiceLifetime.Singleton),
            TypeBinding<TenantEventsManager, TenantEventsManager>(ServiceLifetime.Singleton),
            TypeBinding<DefaultTenantsProvider, DefaultTenantsProvider>(ServiceLifetime.Scoped),
            TypeBinding<DefaultTenantResolver, DefaultTenantResolver>(ServiceLifetime.Scoped),
            TypeBinding<ITenantBackgroundWorkQueue, TenantBackgroundWorkQueue>(ServiceLifetime.Scoped),
            TypeBinding<ITaskExecutor, TaskExecutor>(ServiceLifetime.Scoped),
            TypeBinding<IBackgroundTaskStarter, TaskExecutor>(ServiceLifetime.Scoped),
            TypeBinding<IBackgroundTask, TenantBackgroundWorkQueueWorker>(ServiceLifetime.Scoped),
            Factory<ITenantsProvider>(ServiceLifetime.Scoped)
        ]);
    }

    [Test]
    public async Task Multitenancy_PreservesSharedEventAliasesAndIndependentTaskInstances()
    {
        var services = ApplyFeature(module => new MultitenancyFeature(module));
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(IDistributedLockProvider.Mock().Object);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        var scoped = scope.ServiceProvider;
        var lifecycle = scoped.GetRequiredService<TenantTaskLifecycleEventHandler>();
        await Assert.That(scoped.GetRequiredService<ITenantActivatedEvent>()).IsSameReferenceAs(lifecycle);
        await Assert.That(scoped.GetRequiredService<ITenantDeactivatedEvent>()).IsSameReferenceAs(lifecycle);
        await Assert.That(scoped.GetRequiredService<ITenantsProvider>()).IsSameReferenceAs(scoped.GetRequiredService<DefaultTenantsProvider>());
        await Assert.That(scoped.GetRequiredService<ITaskExecutor>()).IsSameReferenceAs(scoped.GetRequiredService<ITaskExecutor>());
        await Assert.That(ReferenceEquals(scoped.GetRequiredService<ITaskExecutor>(), scoped.GetRequiredService<IBackgroundTaskStarter>())).IsFalse();
    }

    [Test]
    public async Task CommitStrategies_PreservesRegistryAndDeferredStartupRegistration()
    {
        var services = ApplyFeature(module => new CommitStrategiesFeature(module), feature => feature.AddStandardStrategies());
        await AssertBindingsAsync(services,
        [
            TypeBinding<ICommitStrategyRegistry, DefaultCommitStrategyRegistry>(ServiceLifetime.Singleton),
            TypeBinding<IStartupTask, PopulateCommitStrategyRegistry>(ServiceLifetime.Scoped)
        ]);

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        var registry = scope.ServiceProvider.GetRequiredService<ICommitStrategyRegistry>();
        await Assert.That(registry.ListWorkflowStrategyRegistrations()).IsEmpty();
        await Assert.That(registry.ListActivityStrategyRegistrations()).IsEmpty();
        await scope.ServiceProvider.GetServices<IStartupTask>().Single().ExecuteAsync(CancellationToken.None);
        await Assert.That(registry.ListWorkflowStrategyRegistrations().Count()).IsEqualTo(4);
        await Assert.That(registry.ListActivityStrategyRegistrations().Count()).IsEqualTo(4);
    }

    [Test]
    public async Task Workflows_PreservesExecutionStorageAndSerializationBoundaries()
    {
        var services = new ServiceCollection();
        // Characterize this feature's registrations without activating the
        // dependency formatter's process-global TypeDescriptor mutation.
        new WorkflowsFeature(new Module(services)).Apply();
        await AssertBindingsAsync(services,
        [
            TypeBinding<IOutputConverterRegistry, OutputConverterRegistry>(ServiceLifetime.Singleton),
            TypeBinding<IActivityInvoker, ActivityInvoker>(ServiceLifetime.Scoped),
            TypeBinding<IWorkflowRunner, WorkflowRunner>(ServiceLifetime.Scoped),
            TypeBinding<IActivityVisitor, ActivityVisitor>(ServiceLifetime.Scoped),
            TypeBinding<IWorkflowStateExtractor, WorkflowStateExtractor>(ServiceLifetime.Scoped),
            Factory<ICommitStateHandler>(ServiceLifetime.Scoped),
            Factory<IIdentityGenerator>(ServiceLifetime.Singleton),
            TypeBinding<IActivityRegistry, ActivityRegistry>(ServiceLifetime.Singleton),
            TypeBinding<WorkflowBuilder, WorkflowBuilder>(ServiceLifetime.Transient),
            Factory<Func<IWorkflowBuilder>>(ServiceLifetime.Scoped),
            TypeBinding<IWorkflowBuilderFactory, WorkflowBuilderFactory>(ServiceLifetime.Scoped),
            TypeBinding<IIncidentStrategy, FaultStrategy>(ServiceLifetime.Transient),
            TypeBinding<IIncidentStrategy, ContinueWithIncidentsStrategy>(ServiceLifetime.Transient),
            Factory<IActivityExecutionPipeline>(ServiceLifetime.Scoped),
            Factory<IWorkflowExecutionPipeline>(ServiceLifetime.Scoped),
            TypeBinding<IStorageDriverManager, StorageDriverManager>(ServiceLifetime.Scoped),
            TypeBinding<IStorageDriver, WorkflowStorageDriver>(ServiceLifetime.Scoped),
            TypeBinding<IStorageDriver, WorkflowInstanceStorageDriver>(ServiceLifetime.Scoped),
            TypeBinding<IStorageDriver, MemoryStorageDriver>(ServiceLifetime.Scoped),
            TypeBinding<ISerializationTypeRegistry, SerializationTypeRegistry>(ServiceLifetime.Singleton),
            TypeBinding<IWorkflowStateSerializer, JsonWorkflowStateSerializer>(ServiceLifetime.Singleton),
            TypeBinding<IPayloadSerializer, JsonPayloadSerializer>(ServiceLifetime.Singleton),
            TypeBinding<IActivitySerializer, JsonActivitySerializer>(ServiceLifetime.Singleton)
        ]);
    }

    [Test]
    public async Task Workflows_PreservesConfiguredFactoryLifetimesAndExistingOutputRegistry()
    {
        var services = new ServiceCollection();
        var customRegistry = IOutputConverterRegistry.Mock().Object;
        services.AddSingleton(customRegistry);
        var identityCalls = 0;
        var commitCalls = 0;
        var feature = new WorkflowsFeature(new Module(services))
        {
            IdentityGenerator = _ => { identityCalls++; return new FixedIdentityGenerator(); },
            CommitStateHandler = _ => { commitCalls++; return new NoopCommitStateHandler(); }
        };
        feature.Apply();

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var firstScope = provider.CreateAsyncScope();
        await using var secondScope = provider.CreateAsyncScope();
        var first = firstScope.ServiceProvider;
        var second = secondScope.ServiceProvider;
        await Assert.That(first.GetRequiredService<IOutputConverterRegistry>()).IsSameReferenceAs(customRegistry);
        await Assert.That(first.GetRequiredService<IIdentityGenerator>()).IsSameReferenceAs(second.GetRequiredService<IIdentityGenerator>());
        await Assert.That(identityCalls).IsEqualTo(1);
        await Assert.That(first.GetRequiredService<ICommitStateHandler>()).IsSameReferenceAs(first.GetRequiredService<ICommitStateHandler>());
        await Assert.That(ReferenceEquals(first.GetRequiredService<ICommitStateHandler>(), second.GetRequiredService<ICommitStateHandler>())).IsFalse();
        await Assert.That(commitCalls).IsEqualTo(2);
    }

    [Test]
    public async Task DefaultFormatters_PreservesConfigureSideEffectInFreshProcess()
    {
        if (Environment.GetEnvironmentVariable(IsolatedFormatterMarker) != "child")
        {
            await RunFormatterInFreshProcessAsync();
            return;
        }

        await Assert.That(TypeDescriptor.GetConverter(typeof(Type)) is TypeTypeConverter).IsFalse();
        var services = new ServiceCollection();
        var module = new Module(services);
        module.Configure(_ => new DefaultFormattersFeature(module));
        await Assert.That(services).IsEmpty();
        module.Apply();
        await AssertBindingsAsync(services, [TypeBinding<IFormatter, JsonFormatter>(ServiceLifetime.Singleton)]);
        await Assert.That(TypeDescriptor.GetConverter(typeof(Type))).IsTypeOf<TypeTypeConverter>();

        // The formatter is configured before Apply registrations from the real
        // dependency closure, while tenant activation is inserted ahead of them.
        services = new ServiceCollection();
        module = new Module(services);
        module.Configure(_ => new WorkflowsFeature(module));
        module.Apply();
        await AssertBindingsAsync(services,
        [
            TypeBinding<IFormatter, JsonFormatter>(ServiceLifetime.Singleton),
            TypeBinding<IHostedService, ActivateTenants>(ServiceLifetime.Singleton),
            Instance<TimeProvider>(ServiceLifetime.Singleton),
            TypeBinding<IExpressionEvaluator, ExpressionEvaluator>(ServiceLifetime.Scoped),
            TypeBinding<IHostedService, JobRunnerHostedService>(ServiceLifetime.Singleton),
            TypeBinding<IHostedService, BackgroundCommandSenderHostedService>(ServiceLifetime.Singleton),
            TypeBinding<IHostedService, BackgroundEventPublisherHostedService>(ServiceLifetime.Singleton),
            TypeBinding<ICommitStrategyRegistry, DefaultCommitStrategyRegistry>(ServiceLifetime.Singleton),
            TypeBinding<IWorkflowRunner, WorkflowRunner>(ServiceLifetime.Scoped)
        ]);
    }

    private static ServiceCollection ApplyFeature<T>(Func<IModule, T> factory, Action<T>? configure = null) where T : class, IFeature
    {
        var services = new ServiceCollection();
        var module = new Module(services);
        module.Configure(factory, configure);
        module.Apply();
        return services;
    }

    private static async Task AssertBindingsAsync(IServiceCollection services, Binding[] expected)
    {
        var serviceTypes = expected.Select(x => x.Service).ToHashSet();
        var actual = services.Where(x => serviceTypes.Contains(x.ServiceType)).Select(x =>
            new Binding(x.ServiceType, x.Lifetime, x.ImplementationType,
                x.ImplementationFactory is not null ? "factory" : x.ImplementationInstance is not null ? "instance" : "type")).ToList();
        await Assert.That(actual).IsEquivalentTo(expected, EqualityComparer<Binding>.Default, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    private static Binding TypeBinding<TService, TImplementation>(ServiceLifetime lifetime) => new(typeof(TService), lifetime, typeof(TImplementation), "type");
    private static Binding Factory<TService>(ServiceLifetime lifetime) => new(typeof(TService), lifetime, null, "factory");
    private static Binding Instance<TService>(ServiceLifetime lifetime) => new(typeof(TService), lifetime, null, "instance");
    private sealed record Binding(Type Service, ServiceLifetime Lifetime, Type? Implementation, string Kind);
    private sealed class HostTimeProvider : TimeProvider;
    private sealed class FixedIdentityGenerator : IIdentityGenerator
    {
        public string GenerateId() => "configured";
    }

    private static async Task RunFormatterInFreshProcessAsync()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(typeof(ProductionFeatureDescriptorTests).Assembly.Location);
        startInfo.ArgumentList.Add("--treenode-filter");
        startInfo.ArgumentList.Add($"/*/*/{nameof(ProductionFeatureDescriptorTests)}/{nameof(DefaultFormatters_PreservesConfigureSideEffectInFreshProcess)}");
        startInfo.Environment[IsolatedFormatterMarker] = "child";
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start isolated formatter test.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw new TimeoutException("Isolated formatter test did not complete within 60 seconds.");
        }
        await Assert.That(process.ExitCode).IsEqualTo(0).Because($"Isolated formatter test failed.{Environment.NewLine}{await output}{Environment.NewLine}{await error}");
    }
}
