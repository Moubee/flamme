using Flamme.Expressions.Models;
using Flamme.Extensions;
using Flamme.Testing.Shared;
using Flamme.Workflows;
using System.Threading.Tasks;

namespace Flamme.Activities.UnitTests.Primitives;

public class SetVariableTests
{
    [Test]
    public async Task Should_Set_Variable()
    {
        // Arrange
        const int expected = 42;
        var variable = new Variable("myVar", 0, "myVar");
        var setVariable = new SetVariable
        {
            Variable = variable,
            Value = new(expected)
        };

        // Act
        var context = await ExecuteAsync(setVariable);

        // Assert
        var result = variable.Get(context);
        await Assert.That(result).IsEqualTo(expected);
    }

    [Test]
    public async Task Should_Throw_When_Variable_Is_Null()
    {
        // Arrange
        var setVariable = new SetVariable
        {
            Variable = null,
            Value = new("test value")
        };

        // Act & Assert
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => ExecuteAsync(setVariable));
    }

    [Test]
    public async Task Should_Set_Variable_To_Null_Value()
    {
        // Arrange
        var variable = new Variable<string?>("myVar", "initial value", "myVar");
        var setVariable = new SetVariable
        {
            Variable = variable,
            Value = new(new Literal(null))
        };

        // Act
        var context = await ExecuteAsync(setVariable);

        // Assert
        var result = variable.Get(context);
        await Assert.That(result).IsNull();
    }

    private static async Task<ActivityExecutionContext> ExecuteAsync(IActivity activity)
    {
        return await new ActivityTestFixture(activity).ExecuteAsync();
    }
}
