using Flamme.Expressions.CSharp.Handlers;
using Flamme.Expressions.CSharp.Notifications;
using Flamme.Expressions.CSharp.Options;
using Flamme.Expressions.CSharp.Services;
using Flamme.Expressions.Models;
using Flamme.Extensions;
using Flamme.Mediator.Contracts;
using Flamme.Workflows.Memory;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace Flamme.Expressions.UnitTests.CSharp;

public class GenerateWorkflowVariableAccessorsTests
{
    [Test]
    public async Task PascalizedProperty_ReadsCamelCaseVariable_ByRealName()
    {
        var context = CreateContext(new Variable<string>("orderId", "ORD-1"));
        var evaluator = CreateEvaluator();

        var result = await evaluator.EvaluateAsync("Variables.OrderId", typeof(string), context, new ExpressionEvaluatorOptions());

        await Assert.That(result).IsEqualTo("ORD-1");
    }

    [Test]
    public async Task PascalizedProperty_WritesCamelCaseVariable_WithoutCreatingPascalizedShadow()
    {
        var context = CreateContext(new Variable<string>("orderId", "ORD-1"));
        var evaluator = CreateEvaluator();

        var result = await evaluator.EvaluateAsync(
            """
            Variables.OrderId = "ORD-2";
            Variables.OrderId
            """,
            typeof(string),
            context,
            new ExpressionEvaluatorOptions());

        await Assert.That(result).IsEqualTo("ORD-2");
        await Assert.That(context.GetVariable<string>("orderId")).IsEqualTo("ORD-2");
        await Assert.That(context.GetVariable<string>("OrderId")).IsNull();
    }

    [Test]
    public async Task PascalizedProperty_ReadsAlreadyPascalCaseVariable()
    {
        var context = CreateContext(new Variable<string>("OrderId", "ORD-3"));
        var evaluator = CreateEvaluator();

        var result = await evaluator.EvaluateAsync("Variables.OrderId", typeof(string), context, new ExpressionEvaluatorOptions());

        await Assert.That(result).IsEqualTo("ORD-3");
    }

    private static ExpressionExecutionContext CreateContext(Variable variable)
    {
        var memoryRegister = new MemoryRegister();
        memoryRegister.Declare(variable);
        return new ExpressionExecutionContext(new ServiceCollection().BuildServiceProvider(), memoryRegister);
    }

    private static CSharpEvaluator CreateEvaluator()
    {
        var csharpOptions = new CSharpOptions
        {
            AllowHostCodeExecution = true
        };
        var options = Microsoft.Extensions.Options.Options.Create(csharpOptions);
        var accessors = new GenerateWorkflowVariableAccessors(options);
        var assemblies = new AddAssembliesAndReferencesFromOptions(options);
        var notificationSender = INotificationSender.Mock().Object;

        Mock.Get(notificationSender)
            .SendAsync(Arg.Any<INotification>(), Arg.Any<CancellationToken>())
            .Returns(async (INotification notification, CancellationToken cancellationToken) =>
            {
                if (notification is not EvaluatingCSharp evaluating)
                    return;

                await assemblies.HandleAsync(evaluating, cancellationToken);
                await accessors.HandleAsync(evaluating, cancellationToken);
            });

        return new CSharpEvaluator(notificationSender, options, new MemoryCache(new MemoryCacheOptions()));
    }
}
