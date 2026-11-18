#if ELSA_GENERATED
using Elsa.CodeGeneration.Interceptors;
#endif
using Elsa.Common.Features;
using Elsa.Extensions;
using Elsa.Expressions.CSharp.Features;
using Elsa.Expressions.Liquid.Features;
using Elsa.Features;
using Elsa.Features.Contracts;
using Elsa.Features.Implementations;
using Elsa.Features.Services;
using Elsa.Workflows.Features;
using Elsa.Workflows.Management.Features;
using Elsa.Workflows.Runtime.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Common.IntegrationTests;

public class GeneratedSdkCompositionTests
{
    [Test]
    public async Task TransitiveExpressionModulesSupplyFactoriesBeforeSelection()
    {
        var services = new ServiceCollection();
        var module = services.CreateModule();
#if ELSA_GENERATED
        var catalog = ((Module)module).Catalog!;
        await Assert.That(catalog.FeatureTypes).Contains(typeof(CSharpFeature));
        await Assert.That(catalog.FeatureTypes).Contains(typeof(LiquidFeature));
#endif
        await Assert.That(module.HasFeature<CSharpFeature>()).IsFalse();
        module.UseCSharp();
        module.Apply();

        await Assert.That(module.HasFeature<CSharpFeature>()).IsTrue();
        await Assert.That(module.HasFeature<MediatorFeature>()).IsTrue();
        await Assert.That(module.HasFeature<LiquidFeature>()).IsFalse();
    }

    [Test]
    public async Task ConfigureBeforeAddUsesTheCompiledCatalogAndRunsTheApplicationCallbackOnce()
    {
        var services = new ServiceCollection();
        List<string> configured = [];
        var first = services.ConfigureElsa(_ => configured.Add("before"));
        var applied = services.AddElsa(elsa => elsa.UseWorkflows(_ => configured.Add("workflows")));
        services.ConfigureElsa(_ => configured.Add("after"));
        var repeated = services.AddElsa();
        // An ordinary call and an explicit provider call must share the first module.
#if ELSA_GENERATED
        var explicitModule = services.ConfigureElsa(null, ElsaFeatureInterceptors.Catalog);
#elif ELSA_DYNAMIC
        var explicitModule = services.ConfigureElsa();
#else
#error The SDK composition tests require the feature tooling to select an execution mode.
#endif

        await Assert.That(ReferenceEquals(first, applied) && ReferenceEquals(applied, repeated) &&
            ReferenceEquals(repeated, explicitModule)).IsTrue();
        await Assert.That(string.Join(",", configured)).IsEqualTo("before,workflows");
        await Assert.That(applied.HasFeature<AppFeature>()).IsTrue();
        await Assert.That(applied.HasFeature<WorkflowManagementFeature>()).IsTrue();
        await Assert.That(applied.HasFeature<WorkflowRuntimeFeature>()).IsTrue();
        await Assert.That(applied.HasFeature<MediatorFeature>()).IsTrue();
        await Assert.That(applied.HasFeature<WorkflowsFeature>()).IsTrue();
        using var provider = services.BuildServiceProvider();
        await Assert.That(provider.GetRequiredService<IInstalledFeatureProvider>().List().Count()).IsGreaterThan(5);

        var fresh = services.CreateModule();
        await Assert.That(fresh.HasFeature<AppFeature>()).IsFalse();
        await Assert.That(ReferenceEquals(fresh, services.CreateModule())).IsFalse();

        var otherProvider = new ExplicitFeatureCatalogBuilder().Build();
        await Assert.That(() => services.ConfigureElsa(null, otherProvider))
            .Throws<InvalidOperationException>().WithMessageContaining("different feature provider");
        await Assert.That(ReferenceEquals(new ServiceCollection().ConfigureElsa(), first)).IsFalse();
    }
}
