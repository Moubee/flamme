using Flamme.Common;
using Flamme.Common.Features;
using Flamme.Common.Serialization;
using Flamme.Expressions.Features;
using Flamme.Extensions;
using Flamme.Features.Abstractions;
using Flamme.Features.Attributes;
using Flamme.Features.Services;
using Flamme.Workflows.ActivationValidators;
using Flamme.Workflows.Activities.Flowchart.Models;
using Flamme.Workflows.Builders;
using Flamme.Workflows.CommitStates;
using Flamme.Workflows.Exceptions;
using Flamme.Workflows.IncidentStrategies;
using Flamme.Workflows.LogPersistence;
using Flamme.Workflows.LogPersistence.Strategies;
using Flamme.Workflows.Middleware.Activities;
using Flamme.Workflows.Middleware.Workflows;
using Flamme.Workflows.Options;
using Flamme.Workflows.Pipelines.ActivityExecution;
using Flamme.Workflows.Pipelines.WorkflowExecution;
using Flamme.Workflows.PortResolvers;
using Flamme.Workflows.Serialization.Configurators;
using Flamme.Workflows.Serialization.Helpers;
using Flamme.Workflows.Serialization.Serializers;
using Flamme.Workflows.Services;
using Flamme.Workflows.State;
using Flamme.Workflows.UIHints.CheckList;
using Flamme.Workflows.UIHints.Dictionary;
using Flamme.Workflows.UIHints.Dropdown;
using Flamme.Workflows.UIHints.JsonEditor;
using Flamme.Workflows.UIHints.RadioList;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Newtonsoft.Json.Linq;

namespace Flamme.Workflows.Features;

/// <summary>
/// Adds workflow services to the system.
/// </summary>
[DependsOn(typeof(TimeProviderFeature))]
[DependsOn(typeof(ExpressionsFeature))]
[DependsOn(typeof(MediatorFeature))]
[DependsOn(typeof(DefaultFormattersFeature))]
[DependsOn(typeof(MultitenancyFeature))]
[DependsOn(typeof(CommitStrategiesFeature))]
public class WorkflowsFeature : FeatureBase
{
    /// <inheritdoc />
    public WorkflowsFeature(IModule module) : base(module)
    {
    }

    /// <summary>
    /// A factory that instantiates a concrete <see cref="IStandardInStreamProvider"/>.
    /// </summary>
    public Func<IServiceProvider, IStandardInStreamProvider> StandardInStreamProvider { get; set; } = _ => new StandardInStreamProvider(Console.In);

    /// <summary>
    /// A factory that instantiates a concrete <see cref="IStandardOutStreamProvider"/>.
    /// </summary>
    public Func<IServiceProvider, IStandardOutStreamProvider> StandardOutStreamProvider { get; set; } = _ => new StandardOutStreamProvider(Console.Out);

    /// <summary>
    /// A factory that instantiates a concrete <see cref="IIdentityGenerator"/>.
    /// </summary>
    public Func<IServiceProvider, IIdentityGenerator> IdentityGenerator { get; set; } = sp => new RandomLongIdentityGenerator();

    /// <summary>
    /// A handler for committing workflow execution state.
    /// </summary>
    public Func<IServiceProvider, ICommitStateHandler> CommitStateHandler { get; set; } = sp => new NoopCommitStateHandler();

    /// <summary>
    /// A factory that instantiates a concrete <see cref="ILoggerStateGenerator{WorkflowExecutionContext}"/>.
    /// </summary>
    public Func<IServiceProvider, ILoggerStateGenerator<WorkflowExecutionContext>> WorkflowLoggerStateGenerator { get; set; } = sp => new WorkflowLoggerStateGenerator();

    /// <summary>
    /// A factory that instantiates a concrete <see cref="ILoggerStateGenerator{ActivityExecutionContext}"/>.
    /// </summary>
    public Func<IServiceProvider, ILoggerStateGenerator<ActivityExecutionContext>> ActivityLoggerStateGenerator { get; set; } = sp => new ActivityLoggerStateGenerator();

    /// <summary>
    /// A delegate to configure the <see cref="IWorkflowExecutionPipeline"/>.
    /// </summary>
    public Action<IWorkflowExecutionPipelineBuilder> WorkflowExecutionPipeline { get; set; } = builder => builder
        .UseExceptionHandling()
        .UseDefaultActivityScheduler();

    /// <summary>
    /// A delegate to configure the <see cref="IActivityExecutionPipeline"/>.
    /// </summary>
    public Action<IActivityExecutionPipelineBuilder> ActivityExecutionPipeline { get; set; } = builder => builder
        .UseLogging()
        .UseExceptionHandling()
        .UseExecutionLogging()
        .UseNotifications()
        .UseDefaultActivityInvoker();

    /// <summary>
    /// Fluent method to set <see cref="StandardInStreamProvider"/>.
    /// </summary>
    public WorkflowsFeature WithStandardInStreamProvider(Func<IServiceProvider, IStandardInStreamProvider> provider)
    {
        StandardInStreamProvider = provider;
        return this;
    }

    /// <summary>
    /// Fluent method to set <see cref="StandardOutStreamProvider"/>.
    /// </summary>
    public WorkflowsFeature WithStandardOutStreamProvider(Func<IServiceProvider, IStandardOutStreamProvider> provider)
    {
        StandardOutStreamProvider = provider;
        return this;
    }

    /// <summary>
    /// Fluent method to set <see cref="IdentityGenerator"/>.
    /// </summary>
    public WorkflowsFeature WithIdentityGenerator(Func<IServiceProvider, IIdentityGenerator> generator)
    {
        IdentityGenerator = generator;
        return this;
    }

    /// <summary>
    /// Fluent method to set <see cref="ILoggerStateGenerator{WorkflowExecutionContext}"/>.
    /// </summary>
    public WorkflowsFeature WithWorkflowLoggerStateGenerator(Func<IServiceProvider, ILoggerStateGenerator<WorkflowExecutionContext>> generator)
    {
        WorkflowLoggerStateGenerator = generator;
        return this;
    }

    /// <summary>
    /// Fluent method to set <see cref="ILoggerStateGenerator{ActivityExecutionContext}"/>.
    /// </summary>
    public WorkflowsFeature WithActivityLoggerStateGenerator(Func<IServiceProvider, ILoggerStateGenerator<ActivityExecutionContext>> generator)
    {
        ActivityLoggerStateGenerator = generator;
        return this;
    }

    /// <summary>
    /// Fluent method to configure the <see cref="IWorkflowExecutionPipeline"/>.
    /// </summary>
    public WorkflowsFeature WithWorkflowExecutionPipeline(Action<IWorkflowExecutionPipelineBuilder> setup)
    {
        WorkflowExecutionPipeline = setup;
        return this;
    }

    /// <summary>
    /// Fluent method to configure the <see cref="IActivityExecutionPipeline"/>.
    /// </summary>
    public WorkflowsFeature WithActivityExecutionPipeline(Action<IActivityExecutionPipelineBuilder> setup)
    {
        ActivityExecutionPipeline = setup;
        return this;
    }

    /// <inheritdoc />
    public override void Apply()
    {
        AddFlammeCore(Services);
    }

    private void AddFlammeCore(IServiceCollection services)
    {
        services.Configure<SerializationTypeOptions>(options =>
        {
            options.AddTypeAlias<ExceptionState>(nameof(ExceptionState));
            options.AddTypeAlias<FaultException>(nameof(FaultException));
            options.AddTypeAlias<VariablesDictionary>(nameof(VariablesDictionary));
            options.AddTypeAlias<Token>(nameof(Token));
            options.RegisterLegacyTypeName(typeof(FlowJoinMode), "Elsa.Workflows.Core.Activities.Flowchart.Models.FlowJoinMode, Elsa.Workflows.Core");
            options.AddTypeAliasWithLegacyName<FlowJoinMode>(nameof(FlowJoinMode));
            options.AddTypeAliasWithLegacyName<WorkflowStorageDriver>(nameof(WorkflowStorageDriver));
            options.AddTypeAliasWithLegacyName<WorkflowInstanceStorageDriver>(nameof(WorkflowInstanceStorageDriver));
            options.AddTypeAliasWithLegacyName<MemoryStorageDriver>(nameof(MemoryStorageDriver));
            options.AddTypeAliasWithLegacyName<FaultStrategy>(nameof(FaultStrategy));
            options.AddTypeAliasWithLegacyName<ContinueWithIncidentsStrategy>(nameof(ContinueWithIncidentsStrategy));
            options.AddTypeAliasWithLegacyName<AllowAlwaysStrategy>(nameof(AllowAlwaysStrategy));
            options.AddTypeAlias<Exception>(nameof(Exception));
            options.AddTypeAlias<ArgumentException>(nameof(ArgumentException));
            options.AddTypeAlias<ArgumentNullException>(nameof(ArgumentNullException));
            options.AddTypeAlias<InvalidOperationException>(nameof(InvalidOperationException));
            options.AddTypeAlias<NullReferenceException>(nameof(NullReferenceException));
            options.AddTypeAlias<OperationCanceledException>(nameof(OperationCanceledException));
            options.AddTypeAlias<TaskCanceledException>(nameof(TaskCanceledException));
            options.AddTypeAlias<TimeoutException>(nameof(TimeoutException));
            options.AddTypeAlias<NotSupportedException>(nameof(NotSupportedException));
            options.AddTypeAlias<JObject>(nameof(JObject));
            options.AddTypeAlias<JArray>(nameof(JArray));
        });

        services.TryAddSingleton<IOutputConverterRegistry, OutputConverterRegistry>();

        services

            // Core.
            .AddScoped<IActivityInvoker, ActivityInvoker>()
            .AddScoped<IWorkflowRunner, WorkflowRunner>()
            .AddScoped<IActivityTestRunner, ActivityTestRunner>()
            .AddScoped<IActivityVisitor, ActivityVisitor>()
            .AddScoped<IIdentityGraphService, IdentityGraphService>()
            .AddScoped<IWorkflowGraphBuilder, WorkflowGraphBuilder>()
            .AddScoped<IWorkflowStateExtractor, WorkflowStateExtractor>()
            .AddScoped<IActivitySchedulerFactory, ActivitySchedulerFactory>()
            .AddSingleton<IWorkflowExecutionContextSchedulerStrategy, WorkflowExecutionContextSchedulerStrategy>()
            .AddSingleton<IActivityExecutionContextSchedulerStrategy, ActivityExecutionContextSchedulerStrategy>()
            .AddScoped(CommitStateHandler)
            .AddSingleton<IHasher, Hasher>()
            .AddSingleton<IStimulusHasher, StimulusHasher>()
            .AddSingleton(IdentityGenerator)
            .AddSingleton<IBookmarkPayloadSerializer>(sp => ActivatorUtilities.CreateInstance<BookmarkPayloadSerializer>(sp))
            .AddSingleton<IActivityDescriber, ActivityDescriber>()
            .AddSingleton<IActivityRegistry, ActivityRegistry>()
            .AddScoped<IActivityRegistryLookupService, ActivityRegistryLookupService>()
            .AddScoped<IOutputBindingDestinationResolver, OutputBindingDestinationResolver>()
            .AddSingleton<IOutputConverterSettingsValidator, OutputConverterSettingsValidator>()
            .AddScoped<IOutputConverterInvoker, OutputConverterInvoker>()
            .AddSingleton<IPropertyDefaultValueResolver, PropertyDefaultValueResolver>()
            .AddSingleton<IPropertyUIHandlerResolver, PropertyUIHandlerResolver>()
            .AddSingleton<IActivityFactory, ActivityFactory>()
            .AddTransient<WorkflowBuilder>()
            .AddScoped(typeof(Func<IWorkflowBuilder>), sp => () => sp.GetRequiredService<WorkflowBuilder>())
            .AddScoped<IWorkflowBuilderFactory, WorkflowBuilderFactory>()
            .AddScoped<IVariablePersistenceManager, VariablePersistenceManager>()
            .AddScoped<IIncidentStrategyResolver, DefaultIncidentStrategyResolver>()
            .AddScoped<IActivityStateFilterManager, DefaultActivityStateFilterManager>()
            .AddScoped<IWorkflowInstanceVariableReader, DefaultWorkflowInstanceVariableReader>()
            .AddScoped<IWorkflowInstanceVariableWriter, DefaultWorkflowInstanceVariableWriter>()
            .AddScoped<DefaultActivityInputEvaluator>()

            // Incident Strategies.
            .AddTransient<IIncidentStrategy, FaultStrategy>()
            .AddTransient<IIncidentStrategy, ContinueWithIncidentsStrategy>()

            // Pipelines.
            .AddScoped<IActivityExecutionPipeline>(sp => new ActivityExecutionPipeline(sp, builder =>
            {
                builder.UseActivityExecutionPipelineContributors(sp);
                ActivityExecutionPipeline(builder);
            }))
            .AddScoped<IWorkflowExecutionPipeline>(sp => new WorkflowExecutionPipeline(sp, builder =>
            {
                builder.UseWorkflowExecutionPipelineContributors(sp);
                WorkflowExecutionPipeline(builder);
            }))

            // Built-in activity services.
            .AddScoped<IActivityResolver, PropertyBasedActivityResolver>()
            .AddScoped<IActivityResolver, SwitchActivityResolver>()
            .AddSerializationOptionsConfigurator<AdditionalConvertersConfigurator>()
            .AddSerializationOptionsConfigurator<CustomConstructorConfigurator>()

            // Domain event handlers.
            .AddHandlersFrom<WorkflowsFeature>()

            // Stream providers.
            .AddScoped(StandardInStreamProvider)
            .AddScoped(StandardOutStreamProvider)

            // Storage drivers.
            .AddScoped<IStorageDriverManager, StorageDriverManager>()
            .AddStorageDriver<WorkflowStorageDriver>()
            .AddStorageDriver<WorkflowInstanceStorageDriver>()
            .AddStorageDriver<MemoryStorageDriver>()

            // Serialization.
            .AddSingleton<ISerializationTypeRegistry, SerializationTypeRegistry>()
            .AddSingleton<IWorkflowStateSerializer, JsonWorkflowStateSerializer>()
            .AddSingleton<IPayloadSerializer, JsonPayloadSerializer>()
            .AddSingleton<IActivitySerializer, JsonActivitySerializer>()
            .AddSingleton<IApiSerializer, ApiSerializer>()
            .AddSingleton<ISafeSerializer, SafeSerializer>()
            .AddSingleton<IJsonSerializer, StandardJsonSerializer>()
            .AddSingleton<SyntheticPropertiesWriter>()
            .AddSingleton<ActivityWriter>()

            // Instantiation strategies.
            .AddScoped<IWorkflowActivationStrategy, AllowAlwaysStrategy>()

            // UI.
            .AddScoped<IUIHintHandler, DropDownUIHintHandler>()
            .AddScoped<IUIHintHandler, CheckListUIHintHandler>()
            .AddScoped<IUIHintHandler, RadioListUIHintHandler>()
            .AddScoped<IUIHintHandler, JsonEditorUIHintHandler>()
            .AddScoped<IPropertyUIHandler, StaticCheckListOptionsProvider>()
            .AddScoped<IPropertyUIHandler, StaticRadioListOptionsProvider>()
            .AddScoped<IPropertyUIHandler, StaticDropDownOptionsProvider>()
            .AddScoped<IPropertyUIHandler, JsonCodeOptionsProvider>()
            .AddScoped<DictionaryValueEvaluator>()
            .AddSingleton<IActivityDescriptorModifier, DictionaryUIHintInputModifier>()

            // Logger state generators.
            .AddSingleton(WorkflowLoggerStateGenerator)
            .AddSingleton(ActivityLoggerStateGenerator)

            // Log Persistence Strategies.
            .AddScoped<ILogPersistenceStrategyService, DefaultLogPersistenceStrategyService>()
            .AddScoped<ILogPersistenceStrategy, Include>()
            .AddScoped<ILogPersistenceStrategy, Exclude>()
            .AddScoped<ILogPersistenceStrategy, Inherit>()
            .AddScoped<ILogPersistenceStrategy, Configuration>()

            // Logging
            .AddLogging();
    }
}
