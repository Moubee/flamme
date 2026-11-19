using Flamme.Expressions.Contracts;
using Flamme.Expressions.Models;
using Flamme.Workflows.Models;

namespace Flamme.Workflows;

public record ActivityInputEvaluatorContext(
    ActivityExecutionContext ActivityExecutionContext, 
    ExpressionExecutionContext ExpressionExecutionContext, 
    InputDescriptor InputDescriptor, 
    Input Input, 
    IExpressionEvaluator ExpressionEvaluator);