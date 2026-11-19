using Elsa.Expressions.Contracts;
using Elsa.Expressions.Models;
using Elsa.Resilience.Core.UnitTests.TestHelpers;
using Elsa.Resilience.Models;
using Elsa.Resilience.Options;
using Elsa.Resilience.Serialization;
using System.Threading.Tasks;

namespace Elsa.Resilience.Core.UnitTests;

public class ResilienceStrategyConfigEvaluatorTests
{
    private readonly IResilienceStrategyCatalog _catalog = IResilienceStrategyCatalog.Mock().Object;
    private readonly IExpressionEvaluator _expressionEvaluator = IExpressionEvaluator.Mock().Object;
    private readonly ResilienceStrategyConfigEvaluator _evaluator;
    private readonly ExpressionExecutionContext _context = new(IServiceProvider.Mock().Object, null!);

    public ResilienceStrategyConfigEvaluatorTests()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new ResilienceOptions());
        var serializer = new ResilienceStrategySerializer(options);
        _evaluator = new(_catalog, _expressionEvaluator, serializer);
    }

    [Test]
    [DisplayName("Evaluator should return null when config is null")]
    public async Task EvaluateAsync_NullConfig_ReturnsNull()
    {
        var result = await _evaluator.EvaluateAsync(null, _context);
        await Assert.That(result).IsNull();
    }

    [Test]
    [DisplayName("Evaluator in identifier mode should resolve strategy from catalog")]
    public async Task EvaluateAsync_IdentifierMode_WithValidId_ReturnsStrategyFromCatalog()
    {
        var strategy = TestDataFactory.CreateStrategy("test-strategy", "Test Strategy");
        SetupCatalogStrategy("test-strategy", strategy);
        var config = CreateConfig(ResilienceStrategyConfigMode.Identifier, "test-strategy");

        var result = await _evaluator.EvaluateAsync(config, _context);

        await Assert.That(result).IsNotNull();
        await Assert.That(result.Id).IsEqualTo("test-strategy");
        Mock.Get(_catalog).GetAsync("test-strategy", Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
    }

    [Test]
    [DisplayName("Evaluator in identifier mode should return null for invalid strategy ID '$strategyId'")]
    [Arguments("")]
    [Arguments("   ")]
    [Arguments(null)]
    public async Task EvaluateAsync_IdentifierMode_WithInvalidId_ReturnsNull(string? strategyId)
    {
        var config = CreateConfig(ResilienceStrategyConfigMode.Identifier, strategyId);

        var result = await _evaluator.EvaluateAsync(config, _context);

        await Assert.That(result).IsNull();
        Mock.Get(_catalog).GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).WasNeverCalled();
    }

    [Test]
    [DisplayName("Evaluator in expression mode should return null when expression is null")]
    public async Task EvaluateAsync_ExpressionMode_WithNullExpression_ReturnsNull()
    {
        var config = CreateConfig(ResilienceStrategyConfigMode.Expression);

        var result = await _evaluator.EvaluateAsync(config, _context);

        await Assert.That(result).IsNull();
        Mock.Get(_expressionEvaluator).EvaluateAsync<object>(Arg.Any<Expression>(), Arg.Any<ExpressionExecutionContext>(), Arg.Any<ExpressionEvaluatorOptions?>()).WasNeverCalled();
    }

    [Test]
    [DisplayName("Evaluator in expression mode should resolve string IDs from catalog")]
    public async Task EvaluateAsync_ExpressionMode_ReturnsStringId_ResolvesFromCatalog()
    {
        var expression = new Expression("C#", "\"test-strategy\"");
        var strategy = TestDataFactory.CreateStrategy("test-strategy", "Test Strategy");
        SetupExpressionResult(expression, "test-strategy");
        SetupCatalogStrategy("test-strategy", strategy);
        var config = CreateConfig(ResilienceStrategyConfigMode.Expression, expression: expression);

        var result = await _evaluator.EvaluateAsync(config, _context);

        await Assert.That(result).IsNotNull();
        await Assert.That(result.Id).IsEqualTo("test-strategy");
        Mock.Get(_catalog).GetAsync("test-strategy", Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(1));
    }

    [Test]
    [DisplayName("Evaluator in expression mode should return strategy objects directly")]
    public async Task EvaluateAsync_ExpressionMode_ReturnsStrategyObject_ReturnsStrategyDirectly()
    {
        var expression = new Expression("C#", "strategy");
        var strategy = TestDataFactory.CreateStrategy("direct-strategy", "Direct Strategy");
        SetupExpressionResult(expression, strategy);
        var config = CreateConfig(ResilienceStrategyConfigMode.Expression, expression: expression);

        var result = await _evaluator.EvaluateAsync(config, _context);

        await Assert.That(result).IsNotNull();
        await Assert.That(result).IsSameReferenceAs(strategy);
        Mock.Get(_catalog).GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).WasNeverCalled();
    }

    [Test]
    [DisplayName("Evaluator expression '$expressionCode' should return null for an unexpected result type")]
    [Arguments("null", null)]
    [Arguments("42", 42)]
    public async Task EvaluateAsync_ExpressionMode_ReturnsUnexpectedType_ReturnsNull(string expressionCode, object? expressionResult)
    {
        var expression = new Expression("C#", expressionCode);
        SetupExpressionResult(expression, expressionResult);
        var config = CreateConfig(ResilienceStrategyConfigMode.Expression, expression: expression);

        var result = await _evaluator.EvaluateAsync(config, _context);

        await Assert.That(result).IsNull();
    }
    
    private ResilienceStrategyConfig CreateConfig(ResilienceStrategyConfigMode mode, string? strategyId = null, Expression? expression = null)
    {
        return new()
        {
            Mode = mode,
            StrategyId = strategyId,
            Expression = expression
        };
    }

    private void SetupCatalogStrategy(string id, IResilienceStrategy strategy)
    {
        Mock.Get(_catalog).GetAsync(id, Arg.Any<CancellationToken>()).Returns(strategy);
    }

    private void SetupExpressionResult(Expression expression, object? result)
    {
        Mock.Get(_expressionEvaluator).EvaluateAsync<object>(expression, _context, Arg.Any<ExpressionEvaluatorOptions?>()).Returns(result);
    }
}
