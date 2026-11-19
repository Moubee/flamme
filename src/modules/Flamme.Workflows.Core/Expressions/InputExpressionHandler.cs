using System.Diagnostics;
using Flamme.Expressions.Contracts;
using Flamme.Expressions.Models;
using Flamme.Extensions;
using Flamme.Workflows.Memory;
using Flamme.Workflows.Models;

namespace Flamme.Workflows.Expressions;

/// <summary>
/// Handles Input expressions.
/// </summary>
public class InputExpressionHandler : IExpressionHandler
{
    /// <inheritdoc />
    public ValueTask<object?> EvaluateAsync(Expression expression, Type returnType, ExpressionExecutionContext context, ExpressionEvaluatorOptions options)
    {
        object? result = null;
        var inputDefinition = expression.Value as InputDefinition;

        if (inputDefinition != null)
        {
            result = context.GetInput(inputDefinition.Name);
        }

        return ValueTask.FromResult(result);
    }
}