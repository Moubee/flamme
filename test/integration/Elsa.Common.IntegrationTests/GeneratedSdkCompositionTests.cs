#if ELSA_GENERATED
using Elsa.CodeGeneration.Interceptors;
#endif
using Elsa.Common.Features;
using Elsa.Extensions;
using Elsa.Features;
using Elsa.Features.Contracts;
using Elsa.Features.Services;
using Elsa.Workflows.Features;
using Elsa.Workflows.Management.Features;
using Elsa.Workflows.Runtime.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Common.IntegrationTests;

public class GeneratedSdkCompositionTests
{
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
#else
        var explicitModule = services.ConfigureElsa();
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
