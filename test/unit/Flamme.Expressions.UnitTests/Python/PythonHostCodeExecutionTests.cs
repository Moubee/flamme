using Flamme.Expressions.Contracts;
using Flamme.Expressions.Models;
using Flamme.Expressions.Python.Contracts;
using Flamme.Expressions.Python.Options;
using Flamme.Expressions.Python.Services;
using Flamme.Features.Services;
using Flamme.Workflows.Activities;
using Flamme.Workflows.Management.Services;
using Flamme.Testing.Shared;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;

namespace Flamme.Expressions.UnitTests.Python;

public class PythonHostCodeExecutionTests
{
    [Test]
    public async Task Evaluator_BlocksExecution_WhenHostHasNotOptedIn()
    {
        var evaluator = new PythonNetPythonEvaluator(
            Flamme.Mediator.Contracts.INotificationSender.Mock().Object,
            Microsoft.Extensions.Options.Options.Create(new PythonOptions()));
        var context = await new ActivityTestFixture(new WriteLine("test")).BuildAsync();

        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            evaluator.EvaluateAsync("'hello'", typeof(string), context.ExpressionExecutionContext));

        await Assert.That(exception!.Message).Contains(nameof(PythonOptions.AllowHostCodeExecution)).WithComparison(StringComparison.CurrentCulture);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Descriptor_Browsability_FollowsHostOptIn(bool allowHostCodeExecution)
    {
        var services = new ServiceCollection();
        services.AddOptions();
        var module = IModule.Mock().Object;
        Mock.Get(module).Services.Returns(services);
        new Flamme.Expressions.Python.Features.PythonFeature(module)
        {
            PythonOptions = options => options.AllowHostCodeExecution = allowHostCodeExecution
        }.Apply();
        services.AddSingleton<IExpressionDescriptorRegistry, ExpressionDescriptorRegistry>();

        var serviceProvider = services.BuildServiceProvider();
        var registry = serviceProvider.GetRequiredService<IExpressionDescriptorRegistry>();

        var descriptor = await Assert.That(registry.Find("Python")).IsNotNull();
        await Assert.That(descriptor.IsBrowsable).IsEqualTo(allowHostCodeExecution);
    }
}
