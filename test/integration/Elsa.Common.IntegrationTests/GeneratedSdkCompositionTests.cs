using Elsa.Common.Features;
using Elsa.Extensions;
using Elsa.Expressions.CSharp.Activities;
using Elsa.Expressions.CSharp.Features;
using Elsa.Expressions.Liquid.Features;
using Elsa.Features;
using Elsa.Features.Contracts;
using Elsa.Features.Services;
using Elsa.Workflows;
using Elsa.Workflows.Activities;
using Elsa.Workflows.CommitStates;
using Elsa.Workflows.Features;
using Elsa.Workflows.Management.Features;
using Elsa.Workflows.Management.Options;
using Elsa.Workflows.Runtime;
using Elsa.Workflows.Runtime.Features;
using Elsa.Workflows.Runtime.Middleware.Workflows;
using Elsa.Workflows.Runtime.Services;
using Elsa.Workflows.State;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Elsa.Common.IntegrationTests;

public class GeneratedSdkCompositionTests
{
    [Test]
    public async Task RuntimeResolvesTheExecutionCycleAwareCommitHandlerByDefault()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration, ConfigurationManager>();
        services.AddElsa();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommitStateHandler>();
        await Assert.That(handler).IsSameReferenceAs(scope.ServiceProvider.GetRequiredService<ExecutionCycleAwareCommitStateHandler>());
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task RuntimeReleasesTheCycleAfterTheHostCommit(bool withState, bool fail)
    {
        var services = new ServiceCollection();
        var custom = new RecordingCommitHandler(fail);
        services.AddLogging();
        services.AddSingleton<IConfiguration, ConfigurationManager>();
        services.AddElsa(elsa => elsa.UseWorkflows(workflows => workflows.CommitStateHandler = _ => custom));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var scoped = scope.ServiceProvider;
        var handler = scoped.GetRequiredService<ICommitStateHandler>();
        var registry = scoped.GetRequiredService<IExecutionCycleRegistry>();
        var graph = await scoped.GetRequiredService<IWorkflowGraphBuilder>().BuildAsync(new Workflow(new WriteLine("test")));
        var context = await WorkflowExecutionContext.CreateAsync(scoped, graph, "custom-commit");
        var handle = registry.BeginCycle(context.Id, null, default);
        context.TransientProperties[ExecutionCycleTrackingMiddleware.ExecutionCycleHandleKey] = handle;

        Task Commit() => withState ? handler.CommitAsync(context, new WorkflowState()) : handler.CommitAsync(context);
        if (fail)
            await Assert.That(Commit).Throws<InvalidOperationException>().WithMessage("host commit failed");
        else
            await Commit();

        await Assert.That(custom.WithState).IsEqualTo(withState);
        await Assert.That(custom.ActiveDuringCommit).IsEqualTo(1);
        await Assert.That(registry.ActiveCount).IsEqualTo(0);
        await Assert.That(handle.Disposed.IsCompletedSuccessfully).IsTrue();
    }

    [Test]
    public async Task ReapplyingRuntimeKeepsTheHostCommitHandlerScopedAndDisposesItOnce()
    {
        var services = new ServiceCollection();
        List<RecordingCommitHandler> handlers = [];
        services.AddLogging();
        services.AddSingleton<IConfiguration, ConfigurationManager>();
        services.AddElsa(elsa => elsa.UseWorkflows(workflows => workflows.CommitStateHandler = _ =>
        {
            var handler = new RecordingCommitHandler(false);
            handlers.Add(handler);
            return handler;
        }));
        services.AddElsa();
        using var provider = services.BuildServiceProvider();
        for (var i = 0; i < 2; i++)
        {
            using var scope = provider.CreateScope();
            var handler = scope.ServiceProvider.GetRequiredService<ICommitStateHandler>();
            await Assert.That(handler).IsAssignableTo<ExecutionCycleAwareCommitStateHandler>();
            await Assert.That(scope.ServiceProvider.GetRequiredService<ICommitStateHandler>()).IsSameReferenceAs(handler);
        }
        await Assert.That(handlers.Select(handler => handler.DisposeCount).SequenceEqual([1, 1])).IsTrue();
    }

    private sealed class RecordingCommitHandler(bool fail) : ICommitStateHandler, IDisposable
    {
        public bool? WithState { get; private set; }
        public int ActiveDuringCommit { get; private set; }
        public int DisposeCount { get; private set; }

        public void Dispose() => DisposeCount++;
        public Task CommitAsync(WorkflowExecutionContext context, CancellationToken cancellationToken = default) => Record(context, false);
        public Task CommitAsync(WorkflowExecutionContext context, WorkflowState state, CancellationToken cancellationToken = default) => Record(context, true);

        private Task Record(WorkflowExecutionContext context, bool withState)
        {
            WithState = withState;
            ActiveDuringCommit = context.GetRequiredService<IExecutionCycleRegistry>().ActiveCount;
            return fail ? Task.FromException(new InvalidOperationException("host commit failed")) : Task.CompletedTask;
        }
    }

    [Test]
    public async Task AppendingToPipelinesPreservesRuntimeMiddlewareAndHostActions()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration, ConfigurationManager>();
        int? activeDuringWorkflow = null;
        Type? firstActivityMiddleware = null;
        services.AddElsa(elsa => elsa.UseWorkflows(workflows =>
        {
            workflows.WorkflowExecutionPipeline += builder => builder.Use(next => context =>
            {
                activeDuringWorkflow = context.GetRequiredService<IExecutionCycleRegistry>().ActiveCount;
                return next(context);
            });
            workflows.ActivityExecutionPipeline += builder => builder.Insert(0, next =>
            {
                firstActivityMiddleware = next.Target?.GetType();
                return next;
            });
        }));
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var scoped = scope.ServiceProvider;
        await scoped.GetRequiredService<IWorkflowRunner>().RunAsync(new WriteLine("test"));

        await Assert.That(activeDuringWorkflow).IsEqualTo(1);
        await Assert.That(firstActivityMiddleware).IsEqualTo(typeof(Workflows.Middleware.Activities.ExceptionHandlingMiddleware));
        await Assert.That(scoped.GetRequiredService<IExecutionCycleRegistry>().ActiveCount).IsEqualTo(0);
    }

    [Test]
    public async Task HostCanOptIntoReadLineAndRemoveABuiltInActivity()
    {
        var services = new ServiceCollection();
        var module = services.AddElsa(elsa => elsa.UseWorkflowManagement(management => management
            .AddActivity<ReadLine>()
            .RemoveActivity<WriteLine>()));
        var activityTypes = module.Configure<WorkflowManagementFeature>().ActivityTypes;
        await Assert.That(activityTypes.Contains(typeof(ReadLine))).IsTrue();
        await Assert.That(activityTypes.Contains(typeof(WriteLine))).IsFalse();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<ManagementOptions>>().Value;
        await Assert.That(options.ActivityTypes.Contains(typeof(ReadLine))).IsTrue();
        await Assert.That(options.ActivityTypes.Contains(typeof(WriteLine))).IsFalse();
    }

    [Test]
    public async Task AutomaticRegistrationsRespectTheLastExplicitSelectionAcrossFeatures()
    {
        var services = new ServiceCollection();
        services.ConfigureElsa(elsa => elsa.UseWorkflowManagement(management => management
            .RemoveActivity<WriteLine>()
            .RemoveActivity<NotFoundActivity>()
            .RemoveActivity<RunCSharp>()));
        var module = services.AddElsa(elsa => elsa.UseCSharp().AddActivity<WriteLine>());

        var activities = module.Configure<WorkflowManagementFeature>().ActivityTypes;
        await Assert.That(activities.Contains(typeof(WriteLine))).IsTrue();
        await Assert.That(activities.Contains(typeof(ReadLine))).IsFalse();
        await Assert.That(activities.Contains(typeof(NotFoundActivity))).IsFalse();
        await Assert.That(activities.Contains(typeof(RunCSharp))).IsFalse();
    }

    [Test]
    public async Task BuildSelectedFeaturesHaveClosedMembership()
    {
        var services = new ServiceCollection();
        var module = services.CreateModule();
#if ELSA_GENERATED
        await Assert.That(module).IsAssignableTo<FixedFeatureModule>();
        await Assert.That(module.HasFeature<CSharpFeature>()).IsTrue();
        await Assert.That(module.HasFeature<LiquidFeature>()).IsFalse();
#else
        await Assert.That(module.HasFeature<CSharpFeature>()).IsFalse();
#endif
        module.UseCSharp();
        module.Apply();

        await Assert.That(module.HasFeature<CSharpFeature>()).IsTrue();
        await Assert.That(module.HasFeature<MediatorFeature>()).IsTrue();
        await Assert.That(module.HasFeature<LiquidFeature>()).IsFalse();
    }

#if ELSA_GENERATED
    [Test]
    public async Task ACallbackCannotInstallAFeatureOutsideItsModulesPlan()
    {
        var empty = EmptyModule();
        await Assert.That(empty.HasFeature<LiquidFeature>()).IsFalse();
        var constructed = false;
        var configured = false;
        await Assert.That(() => empty.Configure<LiquidFeature>(m =>
        {
            constructed = true;
            return new LiquidFeature(m);
        }, _ => configured = true)).Throws<InvalidOperationException>().WithMessageContaining(nameof(LiquidFeature));
        await Assert.That(constructed).IsFalse();
        await Assert.That(configured).IsFalse();
    }

    private static IModule EmptyModule() => new ServiceCollection().CreateModule();
#endif

    [Test]
    public async Task LoggingRegistrationPreservesTheCollectionPlan()
    {
        var services = new ServiceCollection();
        var first = services.ConfigureElsa(module => module.UseCSharp());
        var applied = services.AddLogging().AddElsa();
        await Assert.That(applied).IsSameReferenceAs(first);
        await Assert.That(applied.HasFeature<CSharpFeature>()).IsTrue();

        var other = new ServiceCollection();
        var withLogging = LoggingServiceCollectionExtensions.AddLogging(other, _ => { });
        var separate = withLogging.ConfigureElsa(module => module.UseLiquid());
        await Assert.That(other.AddElsa()).IsSameReferenceAs(separate);
        await Assert.That(separate.HasFeature<CSharpFeature>()).IsFalse();
        await Assert.That(applied.HasFeature<LiquidFeature>()).IsFalse();
    }

    [Test]
    public async Task ConfigureBeforeAddReusesThePlanAndRunsTheApplicationCallbackOnce()
    {
        var services = new ServiceCollection();
        List<string> configured = [];
        var first = services.ConfigureElsa(_ => configured.Add("before"));
        Action<IWorkflowExecutionPipelineBuilder> customPipeline = _ => { };
        Action<IActivityExecutionPipelineBuilder> customActivityPipeline = _ => { };
        var applied = services.AddElsa(elsa => elsa.UseWorkflows(workflows =>
        {
            configured.Add("workflows");
            workflows.WorkflowExecutionPipeline = customPipeline;
            workflows.ActivityExecutionPipeline = customActivityPipeline;
        }));
        services.ConfigureElsa(_ => configured.Add("after"));
        var repeated = services.AddElsa();
        await Assert.That(ReferenceEquals(first, applied) && ReferenceEquals(applied, repeated)).IsTrue();
        await Assert.That(string.Join(",", configured)).IsEqualTo("before,workflows");
        await Assert.That(applied.HasFeature<AppFeature>()).IsTrue();
        await Assert.That(applied.HasFeature<WorkflowManagementFeature>()).IsTrue();
        await Assert.That(applied.HasFeature<WorkflowRuntimeFeature>()).IsTrue();
        await Assert.That(applied.HasFeature<MediatorFeature>()).IsTrue();
        await Assert.That(applied.HasFeature<WorkflowsFeature>()).IsTrue();
        await Assert.That(ReferenceEquals(applied.Configure<WorkflowsFeature>().WorkflowExecutionPipeline, customPipeline)).IsTrue();
        await Assert.That(ReferenceEquals(applied.Configure<WorkflowsFeature>().ActivityExecutionPipeline, customActivityPipeline)).IsTrue();
        using var provider = services.BuildServiceProvider();
        await Assert.That(provider.GetRequiredService<IInstalledFeatureProvider>().List().Count()).IsGreaterThan(5);

        var fresh = services.CreateModule();
        await Assert.That(fresh.HasFeature<AppFeature>()).IsFalse();
        await Assert.That(ReferenceEquals(fresh, services.CreateModule())).IsFalse();

        await Assert.That(ReferenceEquals(new ServiceCollection().ConfigureElsa(), first)).IsFalse();
    }
}
