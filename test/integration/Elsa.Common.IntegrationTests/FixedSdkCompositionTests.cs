using Elsa.Extensions;
using Elsa.Features.Abstractions;
using Elsa.Expressions.Liquid.Features;
using Elsa.Features.Contracts;
using Elsa.Features.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Common.IntegrationTests;

public class FixedSdkCompositionTests
{
    [Test]
    public async Task ConfigureAndAddShareTheSelectedPlanAndRunHostActionsOnce()
    {
        var services = new ServiceCollection();
        var first = services.ConfigureElsa(m => m.Configure<ConfiguredFeature>(f => f.Notes.Add("before")), Create);
        var applied = services.AddElsa(m => m.Configure<ConfiguredFeature>(f => f.Notes.Add("during")), Create);
        services.ConfigureElsa(m => m.Configure<ConfiguredFeature>(f => f.Notes.Add("after")), Create);
        var repeated = services.AddElsa(null, Create);

        await Assert.That(first).IsSameReferenceAs(applied);
        await Assert.That(repeated).IsSameReferenceAs(applied);
        await Assert.That(string.Join(",", first.Configure<ConfiguredFeature>().Notes)).IsEqualTo("before,during,configure,apply,apply");
        using var provider = services.BuildServiceProvider();
        await Assert.That(provider.GetRequiredService<IInstalledFeatureRegistry>().Find("Example.Configured")).IsNotNull();
        var fresh = new ServiceCollection().ConfigureElsa(null, Create);
        await Assert.That(ReferenceEquals(fresh, applied)).IsFalse();

        await Assert.That(() => services.ConfigureElsa(null, Alternate)).Throws<InvalidOperationException>().WithMessageContaining("different feature provider");
    }

    [Test]
    public async Task AddElsaRejectsAnAbsentSlotBeforeInvokingItsCallbacks()
    {
        var constructed = false;
        var configured = false;
        Action<IModule> callback = module => module.Configure<LiquidFeature>(m =>
        {
            constructed = true;
            return new LiquidFeature(m);
        }, _ => configured = true);

        await Assert.That(() => new ServiceCollection().AddElsa(callback, Create))
            .Throws<InvalidOperationException>().WithMessageContaining(nameof(LiquidFeature));
        await Assert.That(constructed).IsFalse();
        await Assert.That(configured).IsFalse();
    }

    private static FixedFeatureModule Create(IServiceCollection services) => new Plan(services);
    private static FixedFeatureModule Alternate(IServiceCollection services) => new Plan(services);

    private sealed class Plan : FixedFeatureModule
    {
        private readonly FixedFeatureSlot _feature;

        public Plan(IServiceCollection services) : base(services)
        {
            _feature = Declare<ConfiguredFeature>(m => new(m), new("Configured", "Example", "Configured"));
            SealDeclarations();
        }

        protected override void ConfigureFeatures() => _feature.Configure();
        protected override void ApplyFeatures() => _feature.Apply();
    }

    private sealed class ConfiguredFeature(IModule module) : FeatureBase(module)
    {
        public List<string> Notes { get; } = [];
        public override void Configure() => Notes.Add("configure");
        public override void Apply() => Notes.Add("apply");
    }
}
