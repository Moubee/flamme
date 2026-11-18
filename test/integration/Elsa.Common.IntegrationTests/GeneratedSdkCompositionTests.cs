using Elsa.Common.Features;
using Elsa.Extensions;
using Elsa.Expressions.CSharp.Features;
using Elsa.Expressions.Liquid.Features;
using Elsa.Features;
using Elsa.Features.Contracts;
using Elsa.Features.Services;
using Elsa.Workflows;
using Elsa.Workflows.Features;
using Elsa.Workflows.Management.Features;
using Elsa.Workflows.Runtime.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Common.IntegrationTests;

public class GeneratedSdkCompositionTests
{
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
    public async Task ConfigureBeforeAddReusesThePlanAndRunsTheApplicationCallbackOnce()
    {
        var services = new ServiceCollection();
        List<string> configured = [];
        var first = services.ConfigureElsa(_ => configured.Add("before"));
        Action<IWorkflowExecutionPipelineBuilder> customPipeline = _ => { };
        var applied = services.AddElsa(elsa => elsa.UseWorkflows(workflows =>
        {
            configured.Add("workflows");
            workflows.WorkflowExecutionPipeline = customPipeline;
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
        using var provider = services.BuildServiceProvider();
        await Assert.That(provider.GetRequiredService<IInstalledFeatureProvider>().List().Count()).IsGreaterThan(5);

        var fresh = services.CreateModule();
        await Assert.That(fresh.HasFeature<AppFeature>()).IsFalse();
        await Assert.That(ReferenceEquals(fresh, services.CreateModule())).IsFalse();

        await Assert.That(ReferenceEquals(new ServiceCollection().ConfigureElsa(), first)).IsFalse();
    }
}
