using System.Reflection;
using Flamme.Workflows.Activities;
using Flamme.Workflows.Attributes;
using Flamme.Workflows.Models;
using TUnit.Mocks;
using System.Threading.Tasks;

namespace Flamme.Workflows.Core.UnitTests.Services;

public class ActivityDescriberTests
{
    private readonly IActivityDescriber _describer;

    public ActivityDescriberTests()
    {
        var defaultValueResolver = IPropertyDefaultValueResolver.Mock().Object;
        var propertyUIHandlerResolver = IPropertyUIHandlerResolver.Mock().Object;
        Mock.Get(defaultValueResolver).GetDefaultValue(Arg.Any<PropertyInfo>()).Returns((object?)null);
        Mock.Get(propertyUIHandlerResolver).GetUIPropertiesAsync(Arg.Any<PropertyInfo>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
                    .Returns(() => new ValueTask<IDictionary<string, object>>(new Dictionary<string, object>()));

        _describer = new ActivityDescriber(defaultValueResolver, propertyUIHandlerResolver);
    }

    [Test]
    public async Task DescribeActivityAsync_MapsCanContainSecretsToInputDescriptorSensitivity()
    {
        var descriptor = await _describer.DescribeActivityAsync(typeof(ActivityWithSensitiveInputs));

        var sensitiveInput = descriptor.Inputs.Single(x => x.Name == nameof(ActivityWithSensitiveInputs.SensitiveText));
        var publicInput = descriptor.Inputs.Single(x => x.Name == nameof(ActivityWithSensitiveInputs.PublicText));

        using (Assert.Multiple())
        {
            await Assert.That(sensitiveInput.IsSensitive).IsTrue();
            await Assert.That(publicInput.IsSensitive).IsFalse();
        }
    }

    [Test]
    public async Task ClosedGenericIdentityMatchesBothDescriptorAndAuthoredNode()
    {
        var activity = new ForEach<string>();
        var descriptor = await _describer.DescribeActivityAsync(typeof(ForEach<string>));

        await Assert.That(descriptor.TypeName).IsEqualTo("ControlFlow.ForEach<String>");
        await Assert.That(descriptor.Name).IsEqualTo("ForEach<String>");
        await Assert.That(activity.Type).IsEqualTo(descriptor.TypeName);
        await Assert.That(activity.Version).IsEqualTo(descriptor.Version);
    }

    [Test]
    public async Task DerivedTypesOwnTheirActivityIdentityAndVersion()
    {
        var extension = new CustomWriteLine();
        var explicitVersion = new VersionedWriteLine();
        await Assert.That(extension.Type).IsEqualTo(typeof(CustomWriteLine).Namespace + ".CustomWriteLine");
        await Assert.That(explicitVersion.Type).IsEqualTo("Samples.Write");
        await Assert.That(explicitVersion.Version).IsEqualTo(2);
    }

    private sealed class CustomWriteLine() : WriteLine("sample");

    [Activity("Samples", "Write", 2)]
    private sealed class VersionedWriteLine() : WriteLine("sample");

    private sealed class ActivityWithSensitiveInputs : CodeActivity
    {
        [Input(CanContainSecrets = true)]
        public Input<string?> SensitiveText { get; set; } = new("secret");

        [Input]
        public Input<string?> PublicText { get; set; } = new("public");
    }
}