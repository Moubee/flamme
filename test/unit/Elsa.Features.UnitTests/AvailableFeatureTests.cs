using System.Reflection;
using Elsa.Extensions;
using Elsa.Features.Abstractions;
using Elsa.Features.Models;
using Elsa.Features.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Features.UnitTests;

public class AvailableFeatureTests
{
    private static readonly FeatureDescriptor Metadata = new("Example", "Elsa", "Example");

    [Test]
    public async Task AnUnselectedMissingDependencyIsCheckedOnlyUponApplication()
    {
        var catalog = new ExplicitFeatureCatalogBuilder()
            .AddAvailable<BrokenFeature>(m => new(m), Metadata, [typeof(MissingFeature)])
            .AddAvailable<CustomFeature>(null, Metadata)
            .BuildAvailable();
        var module = new ServiceCollection().CreateModule(catalog);
        var selected = module.Configure(m => new CustomFeature(m, 42));
        module.Apply();

        await Assert.That(selected.Value).IsEqualTo(42);
        await Assert.That(module.HasFeature<BrokenFeature>()).IsFalse();
        var otherHost = new ServiceCollection().CreateModule(catalog);
        otherHost.Configure<BrokenFeature>();
        var failure = await Assert.That(otherHost.Apply).Throws<InvalidOperationException>();
        await Assert.That(failure!.Message).Contains(nameof(BrokenFeature));
        await Assert.That(failure.Message).Contains(nameof(MissingFeature));
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task LegacyConstructorFailuresAreWrappedAndAllowRetryWithACustomFactory(bool generated)
    {
        var services = new ServiceCollection();
        var module = generated
            ? services.CreateModule(new ExplicitFeatureCatalogBuilder().AddAvailable<ThrowingFeature>(m => new(m), Metadata).BuildAvailable())
            : services.CreateModule();

        var failure = await Assert.That(() => module.Configure<ThrowingFeature>()).Throws<TargetInvocationException>();
        await Assert.That(failure!.InnerException).IsTypeOf<FormatException>();
        var retry = module.Configure(m => new ThrowingFeature(m, 1));
        await Assert.That(module.Configure<ThrowingFeature>()).IsSameReferenceAs(retry);
    }

    private sealed class BrokenFeature(IModule module) : FeatureBase(module);
    private sealed class MissingFeature(IModule module) : FeatureBase(module);
    private sealed class CustomFeature(IModule module, int value) : FeatureBase(module)
    {
        public int Value { get; } = value;
    }

    private sealed class ThrowingFeature(IModule module, int unused) : FeatureBase(module)
    {
        public ThrowingFeature(IModule module) : this(module, 0) => throw new FormatException();
        public int Value { get; } = unused;
    }
}
