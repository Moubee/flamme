using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using Flamme.Testing.Shared.Activities;
using Flamme.Workflows.Attributes;
using Flamme.Workflows.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Flamme.Workflows.IntegrationTests.Scenarios.ProducerActivities;

public class ActivityAttributeBindingTests
{
    [Test]
    public async Task InheritedInternalAndPrivateAttributesPreserveDescriptorMetadata()
    {
        var type = typeof(DecoratedActivity);
        var binding = ActivityContextRegistry.GetRequiredBinding(type);
        var descriptor = Describe(type);

        var description = descriptor.Attributes.OfType<DescriptionAttribute>().Single();
        await Assert.That(description.GetType()).IsEqualTo(typeof(AnnotatedProducerActivity).GetCustomAttribute<DescriptionAttribute>()!.GetType());
        await Assert.That(descriptor.Description).IsEqualTo(description.Description);
        await Assert.That(descriptor.TypeName).IsEqualTo("Private.Decorated");
        await Assert.That(descriptor.Version).IsEqualTo(3);
        await Assert.That(descriptor.Attributes.OfType<PrivateTagAttribute>().Select(tag => tag.Value).ToArray()).IsEquivalentTo(new[] { "first", "second" });
        await Assert.That(descriptor.Attributes.Any(attribute => attribute is NullableAttribute or NullableContextAttribute)).IsFalse();
        await Assert.That(descriptor.Inputs.Any(input => input.Name == "Amount")).IsTrue();
        await Assert.That(descriptor.Inputs.Any(input => input.Name == "Shared")).IsTrue();
        await Assert.That(descriptor.Ports.Single(port => port.Name == "Next").DisplayName).IsEqualTo("Continue");
        await Assert.That(binding.GetOutcomeName(nameof(DecoratedActivity.Child))).IsEqualTo("Next");
    }

    [Test]
    public async Task GeneratedConstantsKeepTheirBoxedTypes()
    {
        var type = typeof(NumericActivity);
        var descriptor = Describe(type);
        var attribute = descriptor.Attributes.OfType<NumbersAttribute>().Single();
        var reflected = type.GetCustomAttribute<NumbersAttribute>()!;

        await Assert.That(attribute.ConstructorValue).IsEqualTo(reflected.ConstructorValue);
        await Assert.That(attribute.NamedValue).IsEqualTo(reflected.NamedValue);
        await Assert.That(attribute.Values.SequenceEqual(reflected.Values)).IsTrue();
        await Assert.That(descriptor.Inputs.Single().DefaultValue).IsEqualTo((object)(byte)7);
    }

    [Test]
    public async Task PublicAttributeCanReferencePrivateMetadataTypes()
    {
        var attribute = Describe(typeof(PrivateConstantActivity)).Attributes.OfType<NumbersAttribute>().Single();
        await Assert.That(attribute.ConstructorValue).IsEqualTo(typeof(PrivateTagAttribute));
        await Assert.That(attribute.Values.Single()).IsEqualTo((object)PrivateMode.First);
        await Assert.That(attribute.NamedValue).IsEqualTo(typeof(PrivateMode[]));
    }

    private static ActivityDescriptor Describe(Type activityType)
    {
        using var provider = new ServiceCollection().AddSingleton<IPropertyDefaultValueResolver, PropertyDefaultValueResolver>().BuildServiceProvider();
        return ActivityContextRegistry.GetRequiredBinding(activityType).CreateDescriptor(provider.GetRequiredService<IPropertyDefaultValueResolver>());
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    private sealed class PrivateTagAttribute(string value) : Attribute
    {
        public string Value { get; } = value;
    }

    private sealed class PrivateActivityAttribute() : ActivityAttribute("Private", "Decorated", version: 3);
    private sealed class PrivateInputAttribute : InputAttribute;
    private sealed class PrivatePortAttribute() : PortAttribute("Next");

    [PrivateActivity, PrivateTag("first"), PrivateTag("second")]
    public sealed class DecoratedActivity : AnnotatedProducerActivity
    {
        [PrivateInput(Name = "Amount")] public Input<int>? Count { get; set; }
        [PrivateInput(Name = "Shared")] public static Input<int>? SharedCount { get; set; }
        [PrivatePort(DisplayName = "Continue")] public IActivity? Child { get; set; }
    }

    private enum PrivateMode { First }

    [Numbers(typeof(PrivateTagAttribute), PrivateMode.First, NamedValue = typeof(PrivateMode[]))]
    public sealed class PrivateConstantActivity : CodeActivity;

    public sealed class NumbersAttribute(object constructorValue, params object[] values) : Attribute
    {
        public object ConstructorValue { get; } = constructorValue;
        public object[] Values { get; } = values;
        public object? NamedValue { get; set; }
    }

    [Numbers((sbyte)-4, (ushort)65000, (byte)200, NamedValue = (short)-300)]
    public sealed class NumericActivity : CodeActivity
    {
        [Input(DefaultValue = (byte)7)] public Input<byte>? Count { get; set; }
    }
}
