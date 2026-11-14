using Elsa.Expressions.CSharp.Handlers;
using Elsa.Expressions.CSharp.Notifications;
using Elsa.Expressions.CSharp.Options;
using Elsa.Expressions.CSharp.Services;
using Elsa.Expressions.Models;
using Elsa.Extensions;
using Elsa.Mediator.Contracts;
using Elsa.Workflows.Memory;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Expressions.UnitTests.CSharp;

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
        var notificationSender = Mock.Of<INotificationSender>().Object;

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
