using System.Reflection;
using Bpmn.Semantics;
using Elsa.Bpmn.Activities;
using Elsa.Workflows;
using Elsa.Workflows.Models;
using System.Threading.Tasks;

namespace Elsa.Bpmn.UnitTests;

public class BpmnProcessDescriptorTests
{
    [Test]
    [DisplayName("BpmnProcess's descriptor declares Done and Cancelled as its only flow ports")]
    public async Task DescribeActivityAsync_DeclaresDoneAndCancelledAsOnlyFlowPorts()
    {
        var defaultValueResolver = Mock.Of<IPropertyDefaultValueResolver>().Object;
        var propertyUIHandlerResolver = Mock.Of<IPropertyUIHandlerResolver>().Object;

        Mock.Get(defaultValueResolver).GetDefaultValue(Arg.Any<PropertyInfo>()).Returns((object?)null);
        Mock.Get(propertyUIHandlerResolver)
            .GetUIPropertiesAsync(Arg.Any<PropertyInfo>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(() => new ValueTask<IDictionary<string, object>>(new Dictionary<string, object>()));

        var describer = new ActivityDescriber(defaultValueResolver, propertyUIHandlerResolver);

        var descriptor = await describer.DescribeActivityAsync(typeof(BpmnProcess));

        var flowPorts = descriptor.Ports.Where(port => port.Type == PortType.Flow).ToList();

        await Assert.That(flowPorts.Count).IsEqualTo(2);
        await Assert.That(flowPorts).Contains(port => port.Name == BpmnInterpreter.DoneOutcomeName);
        await Assert.That(flowPorts).Contains(port => port.Name == BpmnInterpreter.CancelledOutcomeName);
    }
}
