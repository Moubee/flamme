namespace Flamme.Workflows;

public interface IActivityInputEvaluator
{
    Task<object?> EvaluateAsync(ActivityInputEvaluatorContext context);
}