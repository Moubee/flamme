using Flamme.Expressions.Contracts;
using Flamme.Expressions.Models;
using Flamme.Extensions;
using Flamme.Testing.Shared;
using Flamme.Workflows;
using Flamme.Workflows.Activities;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;

namespace Flamme.Expressions.UnitTests.Services;

public class ExpressionEvaluatorTests
{
    [Test]
    [DisplayName("Evaluates expression with generic type parameter")]
    public async Task EvaluatesExpressionWithGenericTypeParameter()
    {
        // Arrange
        var context = await CreateContextAsync();
        var evaluator = context.GetRequiredService<IExpressionEvaluator>();
        var expression = new Expression("Literal", "Test Value");

        // Act
        var result = await evaluator.EvaluateAsync<string>(expression, context.ExpressionExecutionContext);

        // Assert
        await Assert.That(result).IsEqualTo("Test Value");
    }

    [Test]
    [DisplayName("Evaluates expression with type parameter")]
    public async Task EvaluatesExpressionWithTypeParameter()
    {
        // Arrange
        var context = await CreateContextAsync();
        var evaluator = context.GetRequiredService<IExpressionEvaluator>();
        var expression = new Expression("Literal", 42);

        // Act
        var result = await evaluator.EvaluateAsync(expression, typeof(int), context.ExpressionExecutionContext);

        // Assert
        await Assert.That(result).IsEqualTo(42);
    }

    [Test]
    [DisplayName("Throws when expression type not found in registry")]
    public async Task ThrowsWhenExpressionTypeNotFound()
    {
        // Arrange
        var context = await CreateContextAsync();
        var evaluator = context.GetRequiredService<IExpressionEvaluator>();
        var expression = new Expression("NonExistentType", "value");

        // Act & Assert
        var exception = await Assert.ThrowsExactlyAsync<Exception>(async () =>
            await evaluator.EvaluateAsync<string>(expression, context.ExpressionExecutionContext));

        var exceptionMessage = exception!.Message;
        await Assert.That(exceptionMessage).Contains("Could not find a descriptor for expression type").WithComparison(StringComparison.CurrentCulture);
        await Assert.That(exceptionMessage).Contains("NonExistentType").WithComparison(StringComparison.CurrentCulture);
    }

    [Test]
    [DisplayName("Resolves expression handler via descriptor factory")]
    public async Task ResolvesExpressionHandlerViaDescriptorFactory()
    {
        // Arrange
        var mockHandler = CreateMockHandler("Mocked Result");
        var context = await CreateContextWithMockHandlerAsync("CustomType", mockHandler);
        var evaluator = context.GetRequiredService<IExpressionEvaluator>();
        var expression = new Expression("CustomType", "test");

        // Act
        var result = await evaluator.EvaluateAsync<string>(expression, context.ExpressionExecutionContext);

        // Assert
        await Assert.That(result).IsEqualTo("Mocked Result");
        Mock.Get(mockHandler).EvaluateAsync(
            Arg.Is<Expression>(e => e.Type == "CustomType" && e.Value as string == "test"),
            Arg.Any<Type>(),
            Arg.Any<ExpressionExecutionContext>(),
            Arg.Any<ExpressionEvaluatorOptions>()).WasCalled(Times.Exactly(1));
    }

    [Test]
    [DisplayName("Passes non-null options as default when not provided")]
    public async Task PassesNonNullOptionsAsDefault()
    {
        // Arrange
        var mockHandler = CreateMockHandler("Result");
        var context = await CreateContextWithMockHandlerAsync("TestType", mockHandler);
        var evaluator = context.GetRequiredService<IExpressionEvaluator>();
        var expression = new Expression("TestType", "value");

        // Act
        await evaluator.EvaluateAsync<string>(expression, context.ExpressionExecutionContext);

        // Assert
        Mock.Get(mockHandler).EvaluateAsync(
            Arg.Any<Expression>(),
            Arg.Any<Type>(),
            Arg.Any<ExpressionExecutionContext>(),
            Arg.Is<ExpressionEvaluatorOptions>(o => o != null)).WasCalled(Times.Exactly(1));
    }

    [Test]
    [DisplayName("Wraps handler exceptions in evaluation context")]
    public async Task WrapsHandlerExceptions()
    {
        // Arrange
        var mockHandler = CreateMockHandlerThatThrows(new InvalidOperationException("Handler failed"));
        var context = await CreateContextWithMockHandlerAsync("FailingType", mockHandler);
        var evaluator = context.GetRequiredService<IExpressionEvaluator>();
        var expression = new Expression("FailingType", "bad value");

        // Act & Assert
        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await evaluator.EvaluateAsync<string>(expression, context.ExpressionExecutionContext));

        await Assert.That(exception!.Message).IsEqualTo("Handler failed");
    }

    private static Task<ActivityExecutionContext> CreateContextAsync()
    {
        var activity = new WriteLine("Hello, World!");
        return new ActivityTestFixture(activity).BuildAsync();
    }

    private static async Task<ActivityExecutionContext> CreateContextWithMockHandlerAsync(string expressionType, IExpressionHandler mockHandler)
    {
        var mockDescriptor = new ExpressionDescriptor
        {
            Type = expressionType,
            HandlerFactory = _ => mockHandler
        };

        var mockProvider = IExpressionDescriptorProvider.Mock().Object;
        Mock.Get(mockProvider).GetDescriptors().Returns([mockDescriptor]);

        var activity = new WriteLine("Hello, World!");
        var fixture = new ActivityTestFixture(activity)
            .ConfigureServices(services =>
            {
                services.RemoveWhere(d => d.ServiceType == typeof(IExpressionDescriptorProvider));
                services.AddSingleton(mockProvider);
            });

        return await fixture.BuildAsync();
    }

    private static IExpressionHandler CreateMockHandler(object returnValue)
    {
        var mockHandler = IExpressionHandler.Mock().Object;
        Mock.Get(mockHandler).EvaluateAsync(
                Arg.Any<Expression>(),
                Arg.Any<Type>(),
                Arg.Any<ExpressionExecutionContext>(),
                Arg.Any<ExpressionEvaluatorOptions>())
            .Returns(returnValue);
        return mockHandler;
    }

    private static IExpressionHandler CreateMockHandlerThatThrows(Exception exception)
    {
        var mockHandler = IExpressionHandler.Mock().Object;
        Mock.Get(mockHandler).EvaluateAsync(
                Arg.Any<Expression>(),
                Arg.Any<Type>(),
                Arg.Any<ExpressionExecutionContext>(),
                Arg.Any<ExpressionEvaluatorOptions>())
            .Throws(exception);
        return mockHandler;
    }
}
