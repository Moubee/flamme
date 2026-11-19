using Flamme.Expressions.Contracts;
using Flamme.Expressions.CSharp.Contracts;
using Flamme.Expressions.CSharp.Options;
using Flamme.Expressions.CSharp.Services;
using Flamme.Expressions.Models;
using Flamme.Features.Services;
using Flamme.Testing.Shared;
using Flamme.Workflows.Activities;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;

namespace Flamme.Expressions.UnitTests.CSharp;

public class CSharpHostCodeExecutionTests
{
    [Test]
    public async Task Evaluator_BlocksExecution_WhenHostHasNotOptedIn()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var evaluator = new CSharpEvaluator(
            Flamme.Mediator.Contracts.INotificationSender.Mock().Object,
            Microsoft.Extensions.Options.Options.Create(new CSharpOptions()),
            memoryCache);
        var context = await new ActivityTestFixture(new WriteLine("test")).BuildAsync();

        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            evaluator.EvaluateAsync("\"hello\"", typeof(string), context.ExpressionExecutionContext, new ExpressionEvaluatorOptions()));

        await Assert.That(exception!.Message).Contains(nameof(CSharpOptions.AllowHostCodeExecution)).WithComparison(StringComparison.CurrentCulture);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Descriptor_Browsability_FollowsHostOptIn(bool allowHostCodeExecution)
    {
        var services = new ServiceCollection();
        services.AddOptions();
        services.AddMemoryCache();
        var module = IModule.Mock().Object;
        Mock.Get(module).Services.Returns(services);
        new Flamme.Expressions.CSharp.Features.CSharpFeature(module)
        {
            CSharpOptions = options => options.AllowHostCodeExecution = allowHostCodeExecution
        }.Apply();
        services.AddSingleton<IExpressionDescriptorRegistry, Flamme.Workflows.Management.Services.ExpressionDescriptorRegistry>();

        var serviceProvider = services.BuildServiceProvider();
        var registry = serviceProvider.GetRequiredService<IExpressionDescriptorRegistry>();

        var descriptor = await Assert.That(registry.Find("CSharp")).IsNotNull();
        await Assert.That(descriptor.IsBrowsable).IsEqualTo(allowHostCodeExecution);
    }
}
