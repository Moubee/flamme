using Flamme.Common;
using Flamme.Common.Multitenancy;
using Flamme.Common.Services;
using Flamme.Common.Serialization;
using Flamme.Expressions.Contracts;
using Flamme.Expressions.Services;
using Flamme.Extensions;
using Flamme.Mediator.Contracts;
using Flamme.Workflows;
using Flamme.Workflows.Activities;
using Flamme.Workflows.CommitStates;
using Flamme.Workflows.Management.Providers;
using Flamme.Workflows.Management.Services;
using Flamme.Workflows.Memory;
using Flamme.Workflows.PortResolvers;
using Flamme.Workflows.Serialization.Serializers;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Flamme.Testing.Shared;

/// <summary>
/// A test fixture for unit testing activities in isolation.
/// Provides a fluent API to configure services, variables, and execution context.
/// </summary>
public class ActivityTestFixture : IAsyncDisposable
{
    private Action<ActivityExecutionContext>? _configureContextAction;
    private readonly List<ServiceProvider> _serviceProviders = [];
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ActivityTestFixture"/> class.
    /// </summary>
    /// <param name="activity">The activity to test</param>
    public ActivityTestFixture(IActivity activity)
    {
        Activity = activity;
        Services = new ServiceCollection();
        AddCoreWorkflowServices(Services);
    }

    /// <summary>
    /// Represents the activity being tested within the context of the activity test fixture.
    /// Provides access to the activity for configuration, execution, and validation purposes.
    /// </summary>
    public IActivity Activity { get; }

    /// <summary>
    /// Gets the service collection for registering additional services.
    /// Use this to add services required by the activity under test.
    /// </summary>
    [UsedImplicitly]
    public IServiceCollection Services { get; private set; }

    /// <summary>
    /// Configures the service collection using a fluent action.
    /// </summary>
    /// <param name="configure">Action to configure the service collection</param>
    /// <returns>The fixture instance for method chaining</returns>
    public ActivityTestFixture ConfigureServices(Action<IServiceCollection> configure)
    {
        configure(Services);
        return this;
    }

    /// <summary>
    /// Configures the activity execution context before execution.
    /// Multiple calls to this method will chain the configuration actions together.
    /// </summary>
    /// <param name="configure">Action to configure the activity execution context</param>
    /// <returns>The fixture instance for method chaining</returns>
    [UsedImplicitly]
    public ActivityTestFixture ConfigureContext(Action<ActivityExecutionContext> configure)
    {
        _configureContextAction += configure;
        return this;
    }

    /// <summary>
    /// Executes the activity and returns the execution context.
    /// </summary>
    /// <returns>The ActivityExecutionContext after execution</returns>
    public async Task<ActivityExecutionContext> ExecuteAsync()
    {
        var context = await BuildAsync();
        return await ExecuteAsync(context);
    }
    
    /// <summary>
    /// Executes the activity using a pre-built <see cref="ActivityExecutionContext"/>.
    /// Useful when you need to customize the context before execution, such as setting initial workflow state or overriding correlation IDs.
    /// </summary>
    /// <param name="context">The pre-built context to execute</param>
    /// <returns>The <see cref="ActivityExecutionContext"/> after execution</returns>
    public async Task<ActivityExecutionContext> ExecuteAsync(ActivityExecutionContext context)
    {
        // Set up variables and inputs, then execute the activity
        await SetupExistingVariablesAsync(Activity, context);
        await context.EvaluateInputPropertiesAsync();
        context.TransitionTo(ActivityStatus.Running);
        await Activity.ExecuteAsync(context);

        return context;
    }

    /// <summary>
    /// Builds the ActivityExecutionContext without executing the activity.
    /// </summary>
    public async Task<ActivityExecutionContext> BuildAsync()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ActivityTestFixture));

        var serviceProvider = Services.BuildServiceProvider();
        try
        {
            var activityRegistry = serviceProvider.GetRequiredService<IActivityRegistry>();
            var workflowGraphBuilder = serviceProvider.GetRequiredService<IWorkflowGraphBuilder>();

            await activityRegistry.RegisterAsync(Activity.GetType());

            var workflow = Workflow.FromActivity(Activity);
            var workflowGraph = await workflowGraphBuilder.BuildAsync(workflow);

            // Create workflow execution context using the static factory method
            var workflowExecutionContext = await WorkflowExecutionContext.CreateAsync(
                serviceProvider,
                workflowGraph,
                $"test-instance-{Guid.NewGuid()}",
                CancellationToken.None
            );

            // Create ActivityExecutionContext for the actual activity we want to test
            var context = await workflowExecutionContext.CreateActivityExecutionContextAsync(Activity);

            // Apply any context configuration action
            _configureContextAction?.Invoke(context);
            _serviceProviders.Add(serviceProvider);

            return context;
        }
        catch
        {
            await serviceProvider.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// Disposes the service providers owned by contexts built by this fixture.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        List<Exception>? disposalExceptions = null;

        for (var i = _serviceProviders.Count - 1; i >= 0; i--)
        {
            try
            {
                await _serviceProviders[i].DisposeAsync();
            }
            catch (Exception exception)
            {
                (disposalExceptions ??= []).Add(exception);
            }
        }

        _serviceProviders.Clear();

        if (disposalExceptions != null)
        {
            throw new AggregateException("One or more activity test service providers failed to dispose.", disposalExceptions);
        }
    }

    /// <summary>
    /// Sets up existing variables found on the activity in the execution context.
    /// This is necessary because in unit tests, variables need to be initialized.
    /// </summary>
    private static Task SetupExistingVariablesAsync(IActivity activity, ActivityExecutionContext context)
    {
        var activityType = activity.GetType();
        var variableProperties = activityType.GetProperties()
            .Where(p => typeof(Variable).IsAssignableFrom(p.PropertyType))
            .ToList();

        foreach (var variable in variableProperties.Select(property => (Variable?)property.GetValue(activity)))
        {
            if(variable == null)
                continue;
            
            context.WorkflowExecutionContext.MemoryRegister.Declare(variable);
            variable.Set(context.ExpressionExecutionContext, variable.Value);
        }

        return Task.CompletedTask;
    }

    private static void AddCoreWorkflowServices(IServiceCollection services)
    {
        services.AddLogging();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(DateTimeOffset.UnixEpoch));
        services.AddSingleton<INotificationSender>(_ => IFixtureNotificationSender.Mock().Object);
        services.AddSingleton<IActivityVisitor, ActivityVisitor>();
        services.AddScoped<IExpressionEvaluator, ExpressionEvaluator>();
        services.AddSingleton<IWellKnownTypeRegistry, WellKnownTypeRegistry>();
        services.AddSingleton<IActivityDescriber, ActivityDescriber>();
        services.AddSingleton<IPropertyDefaultValueResolver, PropertyDefaultValueResolver>();
        services.AddSingleton<IPropertyUIHandlerResolver, PropertyUIHandlerResolver>();
        services.AddSingleton<IActivityRegistry, ActivityRegistry>();
        services.AddScoped<IActivityRegistryLookupService, ActivityRegistryLookupService>();
        services.AddScoped<IIdentityGraphService, IdentityGraphService>();
        services.AddScoped<IWorkflowGraphBuilder, WorkflowGraphBuilder>();
        services.AddScoped<IActivityResolver, PropertyBasedActivityResolver>();
        services.AddScoped<IActivityResolver, SwitchActivityResolver>();
        services.AddScoped<DefaultActivityInputEvaluator>();
        services.AddSingleton<IExpressionDescriptorProvider, DefaultExpressionDescriptorProvider>();
        services.AddSingleton<IExpressionDescriptorRegistry, ExpressionDescriptorRegistry>();
        services.AddSingleton<IIdentityGenerator>(_ => IFixtureIdentityGenerator.Mock().Object);
        services.AddSingleton<IHasher>(_ => IFixtureHasher.Mock().Object);
        services.AddSingleton<IStimulusHasher, StimulusHasher>();
        services.AddSingleton<ICommitStateHandler>(_ => IFixtureCommitStateHandler.Mock().Object);
        services.AddSingleton<IActivitySchedulerFactory, ActivitySchedulerFactory>();
        services.AddSingleton<IWorkflowExecutionContextSchedulerStrategy, FakeWorkflowExecutionContextSchedulerStrategy>();
        services.AddSingleton<IActivityExecutionContextSchedulerStrategy, FakeActivityExecutionContextSchedulerStrategy>();
        services.AddSingleton<ITenantAccessor, DefaultTenantAccessor>();
        services.AddScoped<IWorkflowStateExtractor, WorkflowStateExtractor>();
        services.AddOptions();
        services.AddSingleton<ISerializationTypeRegistry, SerializationTypeRegistry>();
        services.AddSingleton<IPayloadSerializer, JsonPayloadSerializer>();
    }

    // TUnit follows the target's visibility for generated setup extensions. Keep fixture
    // defaults internal so consumers can mock the public interfaces without ambiguous extensions.
    internal interface IFixtureNotificationSender : INotificationSender;
    internal interface IFixtureIdentityGenerator : IIdentityGenerator;
    internal interface IFixtureHasher : IHasher;
    internal interface IFixtureCommitStateHandler : ICommitStateHandler;
}
