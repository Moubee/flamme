using System.Reflection;
using Elsa.Workflows.Attributes;
using Elsa.Workflows.Models;
using TUnit.Mocks;
using System.Threading.Tasks;

namespace Elsa.Workflows.Core.UnitTests.Services;

public class ActivityDescriberTests
{
    private readonly IActivityDescriber _describer;

    public ActivityDescriberTests()
    {
        var defaultValueResolver = Mock.Of<IPropertyDefaultValueResolver>().Object;
        var propertyUIHandlerResolver = Mock.Of<IPropertyUIHandlerResolver>().Object;
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

        await Assert.That(sensitiveInput.IsSensitive).IsTrue();
        await Assert.That(publicInput.IsSensitive).IsFalse();
    }

    private sealed class ActivityWithSensitiveInputs : CodeActivity
    {
        [Input(CanContainSecrets = true)]
        public Input<string?> SensitiveText { get; set; } = new("secret");

        [Input]
        public Input<string?> PublicText { get; set; } = new("public");
    }
}